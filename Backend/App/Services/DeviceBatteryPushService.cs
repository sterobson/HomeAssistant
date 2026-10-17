using HomeAssistant.Shared;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>
/// Sends the house's replaceable battery levels up once a day so their decline can be seen
/// over time.
///
/// The entities blob already carries current levels, but it is overwritten on every push, so
/// it can say what a battery reads today and nothing about where it is heading. The boiler
/// fingerbot's cell died on 2026-10-08 and went unnoticed for nine days; a daily record is
/// what makes that visible.
/// </summary>
internal partial class DeviceBatteryPushService
{
    /// <summary>
    /// Battery sensors belonging to replaceable cells. The "_2"/"_3" suffixes matter: the
    /// living room and dining room thermometers are sensor.sonoff_snzb_02d_battery_2 and _3,
    /// and a plain "_battery" test would silently miss two of the seven room thermometers.
    /// </summary>
    [GeneratedRegex(@"_battery(_\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BatteryEntitySuffix();

    private readonly IHaContext _ha;
    private readonly IDeviceBatteryApiClient _apiClient;
    private readonly WebSynchronisationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeviceBatteryPushService> _logger;

    /// <summary>
    /// The UTC date of the last successful push. UTC deliberately, matching how the storage
    /// buckets rows, so the guard and the stored row always agree on which day it is - a
    /// local-time guard would disagree with storage for an hour either side of midnight
    /// during British Summer Time.
    /// </summary>
    private DateOnly? _lastPushedOn;

    public DeviceBatteryPushService(
        IHaContext ha,
        IDeviceBatteryApiClient apiClient,
        WebSynchronisationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<DeviceBatteryPushService> logger)
    {
        _ha = ha;
        _apiClient = apiClient;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Pushes today's readings unless today's have already gone. Cheap to call often — the
    /// app is restarted for every over-the-air update, so relying on a 24 hour timer alone
    /// would skip days.
    /// </summary>
    public async Task PushIfDueAsync()
    {
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        if (_lastPushedOn == today)
        {
            _logger.LogDebug("Device battery push skipped - already pushed for {Date:yyyy-MM-dd}", today);
            return;
        }

        if (string.IsNullOrEmpty(_configuration.HouseId))
        {
            _logger.LogWarning("Cannot push device batteries - HouseId not configured");
            return;
        }

        List<DeviceBatteryReadingDto> readings = CollectReadings();

        if (readings.Count == 0)
        {
            // Never record an empty day: it would be indistinguishable from "every battery
            // vanished", and Home Assistant may simply not have finished loading yet.
            _logger.LogWarning("Device battery push skipped - no battery sensors found");
            return;
        }

        try
        {
            await _apiClient.PostSnapshotAsync(_configuration.HouseId, new DeviceBatterySnapshotDto
            {
                RecordedAt = _timeProvider.GetUtcNow(),
                Readings = readings
            });

            _lastPushedOn = today;

            int unavailable = readings.Count(reading => !reading.IsAvailable);
            _logger.LogInformation(
                "Pushed {Count} device battery readings ({Unavailable} unavailable) for house {HouseId}",
                readings.Count, unavailable, _configuration.HouseId);
        }
        catch (Exception ex)
        {
            // Leave _lastPushedOn alone so the next scheduled attempt retries today.
            _logger.LogError(ex, "Failed to push device battery readings");
        }
    }

    private List<DeviceBatteryReadingDto> CollectReadings()
    {
        return [.. _ha.GetAllEntities()
            .Where(entity => IsReplaceableBatterySensor(entity.EntityId, GetDeviceClass(entity)))
            .Select(entity => new DeviceBatteryReadingDto
            {
                EntityId = entity.EntityId,
                Name = GetFriendlyName(entity),
                Percent = ParsePercent(entity.EntityState?.State),
                IsAvailable = IsAvailable(entity.EntityState?.State)
            })
            .OrderBy(reading => reading.EntityId, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// True for the battery sensors of devices whose cells someone actually replaces.
    ///
    /// device_class alone is too broad: it also matches phones, the car's state of charge and
    /// the SolaX home battery's capacity sensors, none of which are replaceable cells and all
    /// of which would be noise in a "what needs a new battery" view. Requiring the entity ID
    /// to end in _battery (optionally numbered) excludes those - they end in _battery_level,
    /// _state_of_charge, _target_soc and _capacity respectively - without naming any device.
    /// </summary>
    internal static bool IsReplaceableBatterySensor(string? entityId, string? deviceClass)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        if (!entityId.StartsWith("sensor.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(deviceClass, "battery", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return BatteryEntitySuffix().IsMatch(entityId);
    }

    /// <summary>
    /// The percentage, or null when the sensor reported something that is not one. Note that
    /// "unavailable" and "unknown" parse to null rather than 0 - a silent device is not a flat
    /// battery, and conflating them is how a dead device looks like a healthy one.
    /// </summary>
    internal static double? ParsePercent(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
            ? percent
            : null;
    }

    internal static bool IsAvailable(string? state) => ParsePercent(state).HasValue;

    private static string? GetDeviceClass(Entity entity) => ReadAttribute(entity, "device_class");

    private static string GetFriendlyName(Entity entity) =>
        ReadAttribute(entity, "friendly_name") ?? entity.EntityId;

    private static string? ReadAttribute(Entity entity, string attribute)
    {
        try
        {
            JsonElement? attributes = entity.EntityState?.AttributesJson;
            if (attributes.HasValue && attributes.Value.TryGetProperty(attribute, out JsonElement value))
            {
                return value.GetString();
            }
        }
        catch
        {
            // An entity with malformed attributes should not take the whole push down.
        }

        return null;
    }
}
