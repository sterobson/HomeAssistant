using HomeAssistant.apps;
using HomeAssistant.Shared;
using NetDaemon.HassModel.Entities;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.HouseDevices;

/// <summary>
/// Feeds the web UI's 3D house view (owner, 2026-10-22). On request (<c>house-devices-watch</c>, sent by the page
/// when it opens and every few minutes while it stays open) it uploads a snapshot of every device with its entities'
/// states, then streams state changes for <see cref="WatchFor"/>, batched every <see cref="FlushInterval"/>, so
/// nothing is sent while nobody is looking. It also runs the page's commands (<c>house-device-command</c>) on the
/// entities <see cref="HouseDeviceRules"/> allows, waits for Home Assistant's answer and reports it back.
/// </summary>
internal sealed class HouseDevicesService
{
    internal static readonly TimeSpan WatchFor = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly IHaContext _ha;
    private readonly IHaRegistry _registry;
    private readonly INamedEntities _namedEntities;
    private readonly ISignalRConnectionService _signalRConnection;
    private readonly IHouseDevicesApiClient _apiClient;
    private readonly IZigbeeMeshReader _zigbeeMeshReader;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly IGracefulShutdownService _shutdownService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HouseDevicesService> _logger;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, string?> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private List<HouseDeviceDto> _devices = [];
    private HashSet<string> _shownEntityIds = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _watchUntil = DateTimeOffset.MinValue;
    private IDisposable? _stateSubscription;
    private bool _flushing;

    public HouseDevicesService(
        IHaContext ha,
        IHaRegistry registry,
        INamedEntities namedEntities,
        ISignalRConnectionService signalRConnection,
        IHouseDevicesApiClient apiClient,
        IZigbeeMeshReader zigbeeMeshReader,
        WebSynchronisationConfiguration configuration,
        IGracefulShutdownService shutdownService,
        TimeProvider timeProvider,
        ILogger<HouseDevicesService> logger)
    {
        _ha = ha;
        _registry = registry;
        _namedEntities = namedEntities;
        _signalRConnection = signalRConnection;
        _apiClient = apiClient;
        _zigbeeMeshReader = zigbeeMeshReader;
        _configuration = configuration;
        _shutdownService = shutdownService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Initialize()
    {
        _signalRConnection.On<JsonElement>("house-devices-watch", async _ =>
        {
            try
            {
                await StartWatchingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start the 3D view's device feed");
            }
        });

        _signalRConnection.On<JsonElement>("house-device-command", async data =>
        {
            try
            {
                await HandleCommandAsync(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not run a command from the 3D view");
            }
        });
    }

    /// <summary>Uploads a fresh snapshot of every device, so the page has something to show before anyone opens it.</summary>
    public async Task PushSnapshotAsync()
    {
        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push house devices - HouseId not configured");
            return;
        }

        // The Zigbee hub has no entities, but every signal line ends at it, so it is kept.
        HouseZigbeeDto? zigbee = await _zigbeeMeshReader.ReadAsync();
        HashSet<string> hubs = [.. zigbee?.Nodes.Where(n => n.Role == "coordinator").Select(n => n.DeviceId) ?? []];
        List<HouseDeviceDto> devices = HouseDeviceRules.Describe(ReadDevices(), OwnedEntities(), hubs);
        lock (_lock)
        {
            _devices = devices;
            _shownEntityIds = new HashSet<string>(devices.SelectMany(d => d.Entities).Select(e => e.EntityId), StringComparer.OrdinalIgnoreCase);
        }

        await _apiClient.PostSnapshotAsync(_configuration.HouseId, new HouseDevicesDto
        {
            Devices = devices,
            UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Zigbee = zigbee
        });
        _logger.LogDebug("Pushed {Count} devices for the 3D view", devices.Count);
    }

    private async Task StartWatchingAsync()
    {
        await PushSnapshotAsync();

        bool startFlushing;
        lock (_lock)
        {
            _watchUntil = _timeProvider.GetUtcNow() + WatchFor;
            _stateSubscription ??= _ha.StateAllChanges().Subscribe(change =>
            {
                string entityId = change.Entity.EntityId;
                if (_shownEntityIds.Contains(entityId))
                {
                    _pendingChanges[entityId] = change.New?.State;
                }
            });
            startFlushing = !_flushing;
            _flushing = true;
        }

        if (startFlushing)
        {
            _ = FlushWhileWatchedAsync();
        }
    }

    private async Task FlushWhileWatchedAsync()
    {
        try
        {
            using PeriodicTimer timer = new(FlushInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(_shutdownService.ShutdownToken))
            {
                await FlushAsync();
                lock (_lock)
                {
                    if (_timeProvider.GetUtcNow() >= _watchUntil)
                    {
                        _stateSubscription?.Dispose();
                        _stateSubscription = null;
                        _pendingChanges.Clear();
                        _flushing = false;
                        _logger.LogDebug("3D view feed stopped: nobody asked for it for {Minutes} minutes", WatchFor.TotalMinutes);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "3D view feed failed");
        }

        lock (_lock)
        {
            _stateSubscription?.Dispose();
            _stateSubscription = null;
            _flushing = false;
        }
    }

    private async Task FlushAsync()
    {
        if (_pendingChanges.IsEmpty)
        {
            return;
        }

        List<HouseDeviceStateDto> changes = [];
        foreach (string entityId in _pendingChanges.Keys.ToList())
        {
            if (_pendingChanges.TryRemove(entityId, out string? state))
            {
                changes.Add(new HouseDeviceStateDto { EntityId = entityId, State = state });
            }
        }

        try
        {
            await _apiClient.PostStatesAsync(_configuration.HouseId, new HouseDeviceStatesDto { Changes = changes });
        }
        catch (Exception ex)
        {
            // The next snapshot (the page asks again every few minutes) puts it right.
            _logger.LogDebug(ex, "Could not send {Count} state changes to the 3D view", changes.Count);
        }
    }

    private async Task HandleCommandAsync(JsonElement data)
    {
        HouseDeviceCommandDto? command = data.Deserialize<HouseDeviceCommandDto>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (command == null || string.IsNullOrWhiteSpace(command.EntityId))
        {
            return;
        }

        // Judged against the current registry, never only the last snapshot, so a device that has just been
        // re-paired or handed to the heating is refused.
        List<HouseDeviceDto> devices = HouseDeviceRules.Describe(ReadDevices(), OwnedEntities());
        string? refusal = HouseDeviceRules.RefuseCommand(devices, command.EntityId, command.Action);
        bool ok = false;
        if (refusal == null)
        {
            string domain = HouseDeviceRules.DomainOf(command.EntityId);
            if (_ha is IConfirmedServiceCaller caller)
            {
                ok = await caller.CallServiceConfirmedAsync(domain, command.Action, ServiceTarget.FromEntity(command.EntityId));
            }
            else
            {
                _ha.CallService(domain, command.Action, ServiceTarget.FromEntity(command.EntityId));
                ok = true;
            }
        }

        _logger.LogInformation("3D view asked for {Action} on {EntityId}: {Outcome}",
            command.Action, command.EntityId, refusal ?? (ok ? "done" : "the device did not respond"));

        await _apiClient.PostCommandResultAsync(_configuration.HouseId, new HouseDeviceCommandResultDto
        {
            CommandId = command.CommandId,
            EntityId = command.EntityId,
            Ok = refusal == null && ok,
            Message = refusal ?? (ok ? null : "Home Assistant couldn't reach the device. Try again in a moment.")
        });
    }

    /// <summary>
    /// The devices the app drives by itself, which the 3D view must not fight, each named by one of its entities.
    /// </summary>
    private Dictionary<string, string> OwnedEntities()
    {
        const string heating = "Switched by the heating, which would undo the change at its next check. Use the heating page.";
        Dictionary<string, string> owned = new(StringComparer.OrdinalIgnoreCase)
        {
            // The boiler relay is a Zigbee switch like any plug, so without this the 3D view could fire the boiler.
            [_namedEntities.BoilerRelay.EntityId] = "Switched by the heating, which puts the boiler back as it wants it at every check. Use the heating page.",
            [_namedEntities.KitchenHeaterSmartPlugOnOff.EntityId] = heating,
            [_namedEntities.Bedroom1HeaterSmartPlugOnOff.EntityId] = heating,
            [_namedEntities.DiningRoomHeaterSmartPlugOnOff.EntityId] = heating,
            [_namedEntities.GamesRoomHeaterSmartPlugOnOff.EntityId] = heating,
            // Any entity of a device marks the whole device, so the plug's power sensor stands for its switch.
            [_namedEntities.DiningRoomDehumidifierSmartPlugPower.EntityId] = "Switched by the dehumidifier automation, which would undo the change."
        };
        return owned;
    }

    private List<HouseDeviceReading> ReadDevices()
    {
        List<HouseDeviceReading> devices = [];
        foreach (Device device in _registry.Devices)
        {
            try
            {
                List<HouseEntityReading> entities = [.. device.Entities
                    .Where(entity => !string.IsNullOrEmpty(entity.EntityId))
                    .Select(ReadEntity)];
                string name = (device.NameByUser ?? device.Name ?? device.Id).Trim();
                devices.Add(new HouseDeviceReading(device.Id, name, device.Manufacturer, device.Model, device.Area?.Name, entities));
            }
            catch (Exception ex)
            {
                // One odd registry entry must not lose the whole house.
                _logger.LogDebug(ex, "Could not read device {DeviceId}", device.Id);
            }
        }

        return devices;
    }

    private HouseEntityReading ReadEntity(Entity entity)
    {
        EntityState? state = _ha.GetState(entity.EntityId);
        JsonElement? attributes = state?.AttributesJson;
        return new HouseEntityReading(
            entity.EntityId,
            ReadAttribute(attributes, "friendly_name") ?? entity.EntityId,
            state?.State,
            ReadAttribute(attributes, "unit_of_measurement"),
            ReadAttribute(attributes, "device_class"),
            _registry.GetEntityRegistration(entity.EntityId)?.Platform);
    }

    private static string? ReadAttribute(JsonElement? attributes, string name)
    {
        if (attributes is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }
}
