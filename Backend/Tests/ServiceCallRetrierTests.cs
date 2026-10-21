using HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NetDaemon.Runtime;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// Every command the app sends is retried later if Home Assistant answers with an error (owner, 2026-10-21, after
/// the boiler fingerbot's "device did not respond" was only logged). HOME-ASSISTANT.md gotcha 24.
/// </summary>
[TestClass]
public sealed class ServiceCallRetrierTests
{
    private static readonly ServiceCall TrvOpen = new("climate", "set_temperature", ServiceTarget.FromEntity("climate.den_radiator_1_thermostat"), new { temperature = 25.4 });
    private static readonly ServiceCall TrvShut = new("climate", "set_temperature", ServiceTarget.FromEntity("climate.den_radiator_1_thermostat"), new { temperature = 15 });
    private static readonly ServiceCall OtherTrvOpen = new("climate", "set_temperature", ServiceTarget.FromEntity("climate.den_radiator_2_thermostat"), new { temperature = 25.4 });
    private static readonly ServiceCall Notification = new("notify", "persistent_notification", null, new { message = "hello" });

    [TestMethod]
    public async Task AFailedCall_IsSentAgainAfterADelay_UntilItWorks()
    {
        Harness harness = new(failures: 2);

        Task sending = harness.Retrier.Send(TrvOpen);
        await harness.ReleaseDelayAsync();
        await harness.ReleaseDelayAsync();
        await sending;

        harness.Sent.ShouldBe([TrvOpen, TrvOpen, TrvOpen]);
        harness.Delays.ShouldBe([ServiceCallRetrier.RetryDelays[0], ServiceCallRetrier.RetryDelays[1]]);
        harness.Logger.CountMessagesContaining("went through on retry 2").ShouldBe(1);
    }

    [TestMethod]
    public async Task ACallThatWorks_IsSentOnce()
    {
        Harness harness = new(failures: 0);

        await harness.Retrier.Send(TrvOpen);

        harness.Sent.ShouldBe([TrvOpen]);
        harness.Delays.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ANewerCommandToTheSameEntity_CancelsThePendingRetry()
    {
        // 21:01:16 the den valve's 25.4 °C failed; had the room been decided otherwise by the time the retry came,
        // the retry must not undo that.
        Harness harness = new(failures: 1);

        Task sending = harness.Retrier.Send(TrvOpen);
        await harness.Retrier.Send(TrvShut);
        await harness.ReleaseDelayAsync();
        await sending;

        harness.Sent.ShouldBe([TrvOpen, TrvShut]);
        harness.Logger.CountMessagesContaining("Not retrying").ShouldBe(1);
    }

    [TestMethod]
    public async Task ACommandToAnotherEntity_DoesNotCancelTheRetry()
    {
        Harness harness = new(failures: 1);

        Task sending = harness.Retrier.Send(TrvOpen);
        await harness.Retrier.Send(OtherTrvOpen);
        await harness.ReleaseDelayAsync();
        await sending;

        harness.Sent.ShouldBe([TrvOpen, OtherTrvOpen, TrvOpen]);
    }

    [TestMethod]
    public async Task ACallWithNoEntity_IsNeverCancelledByAnother()
    {
        Harness harness = new(failures: 1);

        Task sending = harness.Retrier.Send(Notification);
        await harness.Retrier.Send(Notification with { Data = new { message = "another" } });
        await harness.ReleaseDelayAsync();
        await sending;

        harness.Sent.Count.ShouldBe(3, "the first notification is still sent again");
    }

    [TestMethod]
    public async Task ItGivesUpAfterTheLastDelay()
    {
        Harness harness = new(failures: int.MaxValue);

        Task sending = harness.Retrier.Send(TrvOpen);
        for (int i = 0; i < ServiceCallRetrier.RetryDelays.Length; i++)
        {
            await harness.ReleaseDelayAsync();
        }

        await sending;

        harness.Sent.Count.ShouldBe(ServiceCallRetrier.RetryDelays.Length + 1);
        harness.Logger.CountMessagesContaining("Giving up").ShouldBe(1);
    }

    [TestMethod]
    public async Task SendOnce_SaysWhetherItWorked_AndNeverRetries()
    {
        Harness harness = new(failures: 1);

        (await harness.Retrier.SendOnceAsync(TrvOpen)).ShouldBeFalse();
        (await harness.Retrier.SendOnceAsync(TrvOpen)).ShouldBeTrue();

        harness.Sent.ShouldBe([TrvOpen, TrvOpen]);
        harness.Delays.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task SendOnce_CancelsAPendingRetryForTheSameEntity()
    {
        // The fingerbot's presses go through SendOnceAsync; a retry left over from an ordinary TurnOn must not
        // press the boiler again after the app has pressed it itself.
        Harness harness = new(failures: 1);

        Task sending = harness.Retrier.Send(TrvOpen);
        (await harness.Retrier.SendOnceAsync(TrvShut)).ShouldBeTrue();
        await harness.ReleaseDelayAsync();
        await sending;

        harness.Sent.ShouldBe([TrvOpen, TrvShut]);
    }

    [TestMethod]
    public async Task TheRealNetDaemonContext_IsWrappedWithRetries()
    {
        // Fails if a NetDaemon upgrade registers IHaContext so that AddServiceCallRetries no longer finds it.
        IServiceCollection? registered = null;
        IHost host = Host.CreateDefaultBuilder()
            .UseNetDaemonRuntime()
            .ConfigureServices(services =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddServiceCallRetries();
                registered = services;
            })
            .Build();

        ServiceDescriptor[] contexts = [.. registered!.Where(d => d.ServiceType == typeof(IHaContext))];
        contexts.Length.ShouldBe(1);
        contexts[0].ImplementationFactory.ShouldNotBeNull();
        registered!.ShouldContain(d => d.ServiceType == typeof(ServiceCallRetrier));

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IHaContext context = scope.ServiceProvider.GetRequiredService<IHaContext>();
        context.ShouldBeOfType<RetryingHaContext>();
        new Entity(context, "switch.test").HaContext.ShouldBeAssignableTo<IConfirmedServiceCaller>("so the fingerbot's presses can be confirmed");
    }


    [TestMethod]
    public void AddServiceCallRetries_RefusesToRunWithoutAContextToWrap()
    {
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddServiceCallRetries());
    }

    /// <summary>A retrier whose sends fail a set number of times and whose delays wait until the test releases them.</summary>
    private sealed class Harness
    {
        private int _failuresLeft;
        private readonly Queue<TaskCompletionSource> _pendingDelays = new();
        private readonly SemaphoreSlim _delayRequested = new(0);

        public List<ServiceCall> Sent { get; } = [];
        public List<TimeSpan> Delays { get; } = [];
        public RecordingLogger<ServiceCallRetrier> Logger { get; } = new();
        public ServiceCallRetrier Retrier { get; }

        public Harness(int failures)
        {
            _failuresLeft = failures;
            Retrier = new ServiceCallRetrier(SendAsync, DelayAsync, Logger);
        }

        private Task SendAsync(ServiceCall call)
        {
            Sent.Add(call);
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException("Failed command (call_service) error: device did not respond");
            }

            return Task.CompletedTask;
        }

        private Task DelayAsync(TimeSpan delay)
        {
            Delays.Add(delay);
            TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingDelays.Enqueue(pending);
            _delayRequested.Release();
            return pending.Task;
        }

        /// <summary>Waits for the retrier to ask for a delay, then lets it pass.</summary>
        public async Task ReleaseDelayAsync()
        {
            (await _delayRequested.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the retrier should be waiting to retry");
            _pendingDelays.Dequeue().SetResult();
        }
    }
}
