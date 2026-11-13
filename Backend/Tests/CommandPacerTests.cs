using HomeAssistant.Services;
using NetDaemon.HassModel.Entities;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The Zigbee network can be overloaded by too many commands at once, and the heating has a lot of radiator valves, so
/// the app sends its Zigbee commands 3 seconds apart, starting with the boiler (owner, 2026-11-13).
/// </summary>
[TestClass]
public sealed class CommandPacerTests
{
    private static ServiceCall Valve(string name, double temperature = 25.4) =>
        new("climate", "set_temperature", ServiceTarget.FromEntity($"climate.{name}_thermostat"), new { temperature });

    private static readonly ServiceCall Relay = new("switch", "turn_on", ServiceTarget.FromEntity("switch.den_boiler_relay"), null);
    private static readonly ServiceCall Plug = new("switch", "turn_on", ServiceTarget.FromEntity("switch.kitchen_heater_plug"), null);

    // ---- The spacing ----

    [TestMethod]
    public async Task TheFirstCommandAfterAQuietSpell_GoesAtOnce()
    {
        Harness harness = new();

        (await harness.Pacer.WaitForTurnAsync(Valve("den_radiator_1"), replaceable: true, priority: false)).ShouldBeTrue();

        harness.Delays.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task EachCommandAfterThat_WaitsThreeSecondsFromTheOneBefore()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);

        Task<bool> second = harness.Pacer.WaitForTurnAsync(Valve("b"), replaceable: true, priority: false);
        Task<bool> third = harness.Pacer.WaitForTurnAsync(Valve("c"), replaceable: true, priority: false);

        await harness.ReleaseNextDelayAsync();
        (await second).ShouldBeTrue();
        third.IsCompleted.ShouldBeFalse("it has to wait for the one before it");
        await harness.ReleaseNextDelayAsync();
        (await third).ShouldBeTrue();

        harness.Delays.ShouldBe([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)]);
    }

    [TestMethod]
    public async Task ACommandAfterALongGap_GoesAtOnceAgain()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        harness.Clock += TimeSpan.FromMinutes(5);

        await harness.Pacer.WaitForTurnAsync(Valve("b"), replaceable: true, priority: false);

        harness.Delays.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task OnlyTheTimeThatIsLeft_IsWaited()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        harness.Clock += TimeSpan.FromSeconds(1);

        Task<bool> next = harness.Pacer.WaitForTurnAsync(Valve("b"), replaceable: true, priority: false);
        await harness.ReleaseNextDelayAsync();
        await next;

        harness.Delays.ShouldBe([TimeSpan.FromSeconds(2)]);
    }

    [TestMethod]
    public async Task OnlyClimateAndSwitchCommandsToAnEntityAreHeldBack()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);

        ServiceCall notification = new("notify", "persistent_notification", null, new { message = "hi" });
        ServiceCall inverter = new("number", "set_value", ServiceTarget.FromEntity("number.solax_charge_limit"), new { value = 50 });
        ServiceCall climateWithNoEntity = new("climate", "set_temperature", null, new { temperature = 20 });

        (await harness.Pacer.WaitForTurnAsync(notification, replaceable: true, priority: false)).ShouldBeTrue();
        (await harness.Pacer.WaitForTurnAsync(inverter, replaceable: true, priority: false)).ShouldBeTrue();
        (await harness.Pacer.WaitForTurnAsync(climateWithNoEntity, replaceable: true, priority: false)).ShouldBeTrue();
        harness.Delays.ShouldBeEmpty();
    }

    // ---- Who goes first ----

    [TestMethod]
    public async Task AConfirmedCommand_GoesAheadOfValvesThatAreWaiting_StillThreeSecondsAfterTheLastSent()
    {
        // The boiler relay is switched before any valve in a pass, and must not queue behind the valves of the pass before.
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        List<string> order = [];
        Task<bool> b = Track(harness.Pacer.WaitForTurnAsync(Valve("b"), replaceable: true, priority: false), "b", order);
        Task<bool> c = Track(harness.Pacer.WaitForTurnAsync(Valve("c"), replaceable: true, priority: false), "c", order);
        Task<bool> relay = Track(harness.Pacer.WaitForTurnAsync(Relay, replaceable: false, priority: true), "relay", order);

        await harness.ReleaseNextDelayAsync();
        await relay;
        await harness.ReleaseNextDelayAsync();
        await b;
        await harness.ReleaseNextDelayAsync();
        await c;

        order.ShouldBe(["relay", "b", "c"]);
        harness.Delays.ShouldAllBe(d => d == TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task ConfirmedCommands_KeepTheirOwnOrder()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        List<string> order = [];
        Task<bool> valve = Track(harness.Pacer.WaitForTurnAsync(Valve("b"), replaceable: true, priority: false), "valve", order);
        Task<bool> relay = Track(harness.Pacer.WaitForTurnAsync(Relay, replaceable: false, priority: true), "relay", order);
        Task<bool> plug = Track(harness.Pacer.WaitForTurnAsync(Plug, replaceable: false, priority: true), "plug", order);

        await harness.ReleaseNextDelayAsync();
        await relay;
        await harness.ReleaseNextDelayAsync();
        await plug;
        await harness.ReleaseNextDelayAsync();
        await valve;

        order.ShouldBe(["relay", "plug", "valve"]);
    }

    // ---- A device decided again while its command waits ----

    [TestMethod]
    public async Task ANewerCommandToTheSameEntity_TakesTheWaitingOnesPlace_NotASecondTurn()
    {
        // The heating evaluates again whenever a thermometer reports, and a valve that has not moved yet is decided again.
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        List<string> order = [];
        Task<bool> bFirst = Track(harness.Pacer.WaitForTurnAsync(Valve("b", 25.4), replaceable: true, priority: false), "b first", order);
        Task<bool> c = Track(harness.Pacer.WaitForTurnAsync(Valve("c"), replaceable: true, priority: false), "c", order);
        Task<bool> bAgain = Track(harness.Pacer.WaitForTurnAsync(Valve("b", 15), replaceable: true, priority: false), "b again", order);

        (await bFirst).ShouldBeFalse("it was replaced, so it must not be sent");
        await harness.ReleaseNextDelayAsync();
        (await bAgain).ShouldBeTrue("the newer one has the older one's place, ahead of c");
        await harness.ReleaseNextDelayAsync();
        (await c).ShouldBeTrue();

        order.ShouldBe(["b first", "b again", "c"]);
        harness.Delays.Count.ShouldBe(2, "two commands were sent after the first, not three");
    }

    [TestMethod]
    public async Task AConfirmedCommand_IsNeverReplacedNorReplaces()
    {
        Harness harness = new();
        await harness.Pacer.WaitForTurnAsync(Valve("a"), replaceable: true, priority: false);
        Task<bool> first = harness.Pacer.WaitForTurnAsync(Relay, replaceable: false, priority: true);
        Task<bool> second = harness.Pacer.WaitForTurnAsync(Relay, replaceable: false, priority: true);

        await harness.ReleaseNextDelayAsync();
        (await first).ShouldBeTrue();
        await harness.ReleaseNextDelayAsync();
        (await second).ShouldBeTrue();
    }

    // ---- Through the service call retrier, which is where every command goes ----

    [TestMethod]
    public async Task APassOfCommands_IsSentThreeSecondsApart_TheBoilerFirst()
    {
        Harness harness = new();
        List<ServiceCall> sent = [];
        ServiceCallRetrier retrier = new(call => { sent.Add(call); return Task.CompletedTask; }, _ => Task.CompletedTask, new RecordingLogger<ServiceCallRetrier>(), harness.Pacer);

        await retrier.Send(Valve("a"));
        Task b = retrier.Send(Valve("b"));
        Task c = retrier.Send(Valve("c"));
        Task<bool> boiler = retrier.SendOnceAsync(Relay);

        await harness.ReleaseNextDelayAsync();
        (await boiler).ShouldBeTrue();
        await harness.ReleaseNextDelayAsync();
        await b;
        await harness.ReleaseNextDelayAsync();
        await c;

        sent.Select(s => s.SupersessionKey).ShouldBe(
            ["climate.a_thermostat", "switch.den_boiler_relay", "climate.b_thermostat", "climate.c_thermostat"]);
        harness.Delays.ShouldAllBe(d => d == TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task ACommandReplacedWhileItWaited_IsNeverSent()
    {
        Harness harness = new();
        List<ServiceCall> sent = [];
        ServiceCallRetrier retrier = new(call => { sent.Add(call); return Task.CompletedTask; }, _ => Task.CompletedTask, new RecordingLogger<ServiceCallRetrier>(), harness.Pacer);

        await retrier.Send(Valve("a"));
        ServiceCall open = Valve("b", 25.4);
        ServiceCall shut = Valve("b", 15);
        Task first = retrier.Send(open);
        Task second = retrier.Send(shut);

        await first;
        await harness.ReleaseNextDelayAsync();
        await second;

        sent.ShouldNotContain(open);
        sent.ShouldContain(shut);
        sent.Count.ShouldBe(2);
    }

    // Awaiting what this returns means the name has been recorded, so the order is the order the turns came.
    private static async Task<bool> Track(Task<bool> turn, string name, List<string> order)
    {
        bool result = await turn;
        lock (order)
        {
            order.Add(name);
        }

        return result;
    }

    private sealed class Harness
    {
        private readonly Queue<TaskCompletionSource> _pendingDelays = new();
        private readonly SemaphoreSlim _delayRequested = new(0);

        public DateTimeOffset Clock { get; set; } = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public CommandPacer Pacer { get; }

        public Harness()
        {
            Pacer = new CommandPacer(CommandPacer.Spacing, DelayAsync, () => Clock);
        }

        private Task DelayAsync(TimeSpan delay)
        {
            Delays.Add(delay);
            TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingDelays.Enqueue(pending);
            _delayRequested.Release();
            return pending.Task;
        }

        /// <summary>Waits for the pacer to ask for a delay, moves the clock on by it, and lets it pass.</summary>
        public async Task ReleaseNextDelayAsync()
        {
            (await _delayRequested.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the pacer should be waiting");
            Clock += Delays[^_pendingDelays.Count];
            _pendingDelays.Dequeue().SetResult();
        }
    }
}
