using Microsoft.Extensions.DependencyInjection;
using NetDaemon.Client;
using NetDaemon.Client.HomeAssistant.Model;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services;

/// <summary>
/// Sends a command and waits for Home Assistant's answer, for the callers that must know whether it worked and
/// will decide for themselves what to do if it did not. Nothing is retried for them.
/// </summary>
public interface IConfirmedServiceCaller
{
    /// <summary>True if Home Assistant reported success, false if it answered with an error or could not be reached.</summary>
    Task<bool> CallServiceConfirmedAsync(string domain, string service, ServiceTarget? target = null, object? data = null);
}

/// <summary>One service call, as the app asked for it.</summary>
internal sealed record ServiceCall(string Domain, string Service, ServiceTarget? Target, object? Data)
{
    /// <summary>
    /// The entities it changes, which a newer command to the same entities replaces. Null for a call with no entity
    /// target (a notification), which nothing replaces.
    /// </summary>
    public string? SupersessionKey => Target?.EntityIds is { } ids && ids.Any()
        ? string.Join(",", ids.Order(StringComparer.OrdinalIgnoreCase))
        : null;

    public override string ToString() =>
        $"{Domain}.{Service}{(Target?.EntityIds is { } ids && ids.Any() ? " on " + string.Join(", ", ids) : "")}{(Data == null ? "" : " " + JsonSerializer.Serialize(Data))}";
}

/// <summary>
/// Sends every command the app makes and, when Home Assistant answers with an error, sends it again later (owner,
/// 2026-10-21). NetDaemon's own <c>CallService</c> is fire-and-forget: a command Zigbee could not deliver ("Failed to
/// send request: device did not respond") was only logged, and the app carried on as if the device had moved
/// (HOME-ASSISTANT.md gotcha 24). A newer command to the same entities replaces a pending retry, so a retry never
/// undoes something decided since. Shared by every app scope, so that holds across the whole app.
/// </summary>
internal sealed class ServiceCallRetrier
{
    /// <summary>
    /// How long to wait before each retry. A delivery failure is usually a busy or flaky Zigbee route that clears in
    /// seconds to minutes; a device that is still silent after half an hour needs a person.
    /// </summary>
    internal static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15)
    ];

    private readonly Func<ServiceCall, Task> _send;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _latestCallByTarget = new(StringComparer.OrdinalIgnoreCase);
    private long _callCount;

    /// <param name="send">Sends the call and completes when Home Assistant has answered; throws if it answered with an error.</param>
    /// <param name="delay">Waits before a retry.</param>
    internal ServiceCallRetrier(Func<ServiceCall, Task> send, Func<TimeSpan, Task> delay, ILogger logger)
    {
        _send = send;
        _delay = delay;
        _logger = logger;
    }

    public ServiceCallRetrier(IHomeAssistantRunner runner, TimeProvider timeProvider, ILogger<ServiceCallRetrier> logger)
        : this(call => SendThroughRunnerAsync(runner, call), delay => Task.Delay(delay, timeProvider), logger)
    {
    }

    /// <summary>Sends the call in the background, retrying it later if Home Assistant reports an error.</summary>
    public Task Send(ServiceCall call)
    {
        long callNumber = Register(call);
        return SendWithRetriesAsync(call, callNumber);
    }

    /// <summary>Sends the call once and says whether it worked. It still replaces any retry pending for the same entities.</summary>
    public async Task<bool> SendOnceAsync(ServiceCall call)
    {
        Register(call);
        try
        {
            await _send(call);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Home Assistant did not carry out {Call}: {Error}", call, ex.Message);
            return false;
        }
    }

    private async Task SendWithRetriesAsync(ServiceCall call, long callNumber)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _send(call);
                if (attempt > 0)
                {
                    _logger.LogInformation("{Call} went through on retry {Attempt}", call, attempt);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= RetryDelays.Length)
                {
                    _logger.LogError("Giving up on {Call} after {Attempts} attempts: {Error}", call, attempt + 1, ex.Message);
                    return;
                }

                _logger.LogWarning("Home Assistant did not carry out {Call}: {Error}. Trying again in {Delay}",
                    call, ex.Message, Describe(RetryDelays[attempt]));
            }

            await _delay(RetryDelays[attempt]);

            if (IsSuperseded(call, callNumber))
            {
                _logger.LogInformation("Not retrying {Call}: a newer command to the same entity has been sent since", call);
                return;
            }
        }
    }

    private long Register(ServiceCall call)
    {
        lock (_lock)
        {
            long callNumber = ++_callCount;
            if (call.SupersessionKey is { } key)
            {
                _latestCallByTarget[key] = callNumber;
            }

            return callNumber;
        }
    }

    private bool IsSuperseded(ServiceCall call, long callNumber)
    {
        if (call.SupersessionKey is not { } key)
            return false;

        lock (_lock)
        {
            return _latestCallByTarget.TryGetValue(key, out long latest) && latest != callNumber;
        }
    }

    private static string Describe(TimeSpan delay) =>
        delay < TimeSpan.FromMinutes(1) ? $"{delay.TotalSeconds:0} s" : $"{delay.TotalMinutes:0} min";

    internal static async Task SendThroughRunnerAsync(IHomeAssistantRunner runner, ServiceCall call)
    {
        IHomeAssistantConnection connection = runner.CurrentConnection
            ?? throw new InvalidOperationException("Not connected to Home Assistant");

        // NetDaemon's CallServiceAsync extension does NOT wait for the answer: it only logs a failure (found 2026-10-22,
        // when a failed and a timed-out command went unretried). SendCommandAndReturnResponseRawAsync waits up to 20 s
        // and throws on an error answer or on no answer at all (read from NetDaemon 25.46.0's HomeAssistantConnection).
        await connection.SendCommandAndReturnResponseRawAsync(ToCommand(call), CancellationToken.None);
    }

    internal static CallServiceMessage ToCommand(ServiceCall call) => new()
    {
        Domain = call.Domain,
        Service = call.Service,
        ServiceData = call.Data,
        Target = ToHassTarget(call.Target)
    };

    private static HassTarget? ToHassTarget(ServiceTarget? target) => target == null
        ? null
        : new HassTarget
        {
            EntityIds = target.EntityIds?.ToList(),
            DeviceIds = target.DeviceIds?.ToList(),
            AreaIds = target.AreaIds?.ToList(),
            FloorIds = target.FloorIds?.ToList(),
            LabelIds = target.LabelIds?.ToList()
        };
}

/// <summary>
/// NetDaemon's <see cref="IHaContext"/> with every service call sent through <see cref="ServiceCallRetrier"/>, so
/// that every entity the app drives (generated entities, <c>TurnOn()</c>, <c>CallService</c>) gets retries without
/// each call site knowing. Everything else is passed straight through.
/// </summary>
internal sealed class RetryingHaContext(IHaContext inner, ServiceCallRetrier retrier) : IHaContext, IConfirmedServiceCaller, IAsyncDisposable
{
    /// <summary>
    /// The container disposes only what the factory returned, so the wrapped context (NetDaemon's is
    /// IAsyncDisposable, and holds the app's subscriptions) is disposed from here.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (inner is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public IObservable<Event> Events => inner.Events;

    public IObservable<StateChange> StateAllChanges() => inner.StateAllChanges();

    public EntityState? GetState(string entityId) => inner.GetState(entityId);

    public IReadOnlyList<Entity> GetAllEntities() => inner.GetAllEntities();

    public void CallService(string domain, string service, ServiceTarget? target = null, object? data = null) =>
        _ = retrier.Send(new ServiceCall(domain, service, target, data));

    public Task<bool> CallServiceConfirmedAsync(string domain, string service, ServiceTarget? target = null, object? data = null) =>
        retrier.SendOnceAsync(new ServiceCall(domain, service, target, data));

    public Task<JsonElement?> CallServiceWithResponseAsync(string domain, string service, ServiceTarget? target = null, object? data = null) =>
        inner.CallServiceWithResponseAsync(domain, service, target, data);

    public Area? GetAreaFromEntityId(string entityId) => inner.GetAreaFromEntityId(entityId);

    public EntityRegistration? GetEntityRegistration(string entityId) => inner.GetEntityRegistration(entityId);

    public void SendEvent(string eventType, object? data = null) => inner.SendEvent(eventType, data);
}

internal static class ServiceCallRetryRegistration
{
    /// <summary>
    /// Wraps the <see cref="IHaContext"/> NetDaemon registered in <see cref="RetryingHaContext"/>. Must come after
    /// <c>UseNetDaemonRuntime</c> has registered it. Throws if there is none, so a NetDaemon upgrade that changes how
    /// it is registered fails at startup instead of silently dropping the retries.
    /// </summary>
    public static IServiceCollection AddServiceCallRetries(this IServiceCollection services)
    {
        ServiceDescriptor original = services.LastOrDefault(d => d.ServiceType == typeof(IHaContext) && !d.IsKeyedService)
            ?? throw new InvalidOperationException("No IHaContext is registered to add service call retries to. Call AddServiceCallRetries after UseNetDaemonRuntime");

        services.Remove(original);
        services.AddSingleton<ServiceCallRetrier>();
        services.Add(ServiceDescriptor.Describe(
            typeof(IHaContext),
            provider => new RetryingHaContext(CreateOriginal(provider, original), provider.GetRequiredService<ServiceCallRetrier>()),
            original.Lifetime));
        return services;
    }

    private static IHaContext CreateOriginal(IServiceProvider provider, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is IHaContext instance)
            return instance;
        if (original.ImplementationFactory != null)
            return (IHaContext)original.ImplementationFactory(provider);
        return (IHaContext)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
    }
}

/// <summary>
/// HA's websocket <c>call_service</c> command. NetDaemon has the same thing (<c>CallServiceCommand</c>) but keeps it
/// internal, and its public way to send one does not wait for the answer; this is sent with
/// <c>SendCommandAndReturnResponseRawAsync</c>, which does.
/// </summary>
internal sealed record CallServiceMessage : CommandMessage
{
    public CallServiceMessage()
    {
        Type = "call_service";
    }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("service")]
    public required string Service { get; init; }

    [JsonPropertyName("service_data")]
    public object? ServiceData { get; init; }

    [JsonPropertyName("target")]
    public HassTarget? Target { get; init; }
}
