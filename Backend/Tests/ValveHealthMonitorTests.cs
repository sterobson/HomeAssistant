using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public class ValveHealthMonitorTests
{
    private const string DenRadiator1 = "climate.den_radiator_1_thermostat";
    private const string DenRadiator2 = "climate.den_radiator_2_thermostat";
    private const string Bedroom3 = "climate.bedroom_3_radiator_thermostat";

    // 2026-10-23 in British Summer Time, which the readings below were taken in.
    private static readonly TimeSpan Bst = TimeSpan.FromHours(1);

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 23, hour, minute, 0, Bst);

    /// <summary>A valve's own reading at a time: the last value it reported at or before then, as Home Assistant holds it.</summary>
    private static double ReadingAt(IReadOnlyList<(DateTimeOffset At, double Value)> reports, DateTimeOffset time) =>
        reports.Last(report => report.At <= time).Value;

    private static ValveHealthMonitor Create(InMemoryValveHealthStore? store = null) => new(store ?? new InMemoryValveHealthStore());

    /// <summary>A valve the app has open, set well above its reading as the app sets it, in a room at <paramref name="room"/>.</summary>
    private static void Open(IValveHealthMonitor monitor, string valve, double temperature, bool? boilerBelievedOn, DateTimeOffset now, double setpoint = 30, double room = 17) =>
        monitor.Observe(valve, new ValveObservation(true, temperature, setpoint, room), boilerBelievedOn, now);

    /// <summary>A valve the app has shut, set well below its reading.</summary>
    private static void Shut(IValveHealthMonitor monitor, string valve, double temperature, bool? boilerBelievedOn, DateTimeOffset now) =>
        monitor.Observe(valve, new ValveObservation(false, temperature, 7, 18), boilerBelievedOn, now);

    private static ValveHealthRecord? Record(IValveHealthMonitor monitor, string valve, string kind) =>
        monitor.RecordsFor(valve).TryGetValue(kind, out ValveHealthRecord? record) ? record : null;

    private static bool IsFlagged(ValveHealthRecord? record) =>
        record?.LastSeenAt != null && (record.LastOkAt == null || record.LastOkAt < record.LastSeenAt);

    // The afternoon of 2026-10-23, from the recorder: the app pressed the boiler on at 14:23 for the dining room and
    // Bedroom 3, and came to believe it off at 14:37, though it ran on. Den radiator 2 had been shut since 08:34 and
    // climbed from 20.1 to 27.1 °C; Bedroom 3's valve was open throughout and climbed from 16.8 to 21 °C, when it shut.
    private static readonly List<(DateTimeOffset At, double Value)> Radiator2 =
    [
        (At(13, 0), 20.1), (At(14, 30), 20.6), (At(14, 33), 21.1), (At(14, 35), 21.6), (At(14, 37), 22.1),
        (At(14, 39), 22.6), (At(14, 42), 23.1), (At(14, 44), 23.6), (At(14, 48), 24.1), (At(14, 51), 24.6),
        (At(14, 55), 25.1), (At(15, 1), 25.6), (At(15, 8), 26.1), (At(15, 16), 26.6), (At(15, 31), 27.1)
    ];

    private static readonly List<(DateTimeOffset At, double Value)> Bedroom3Readings =
    [
        (At(13, 0), 16.8), (At(14, 37), 17.3), (At(14, 42), 17.8), (At(14, 47), 18.3), (At(14, 52), 18.8),
        (At(14, 58), 19.3), (At(15, 5), 19.8), (At(15, 13), 20.3), (At(15, 23), 20.8), (At(15, 28), 21.0)
    ];

    /// <summary>
    /// Replays that afternoon, one heating pass every 5 minutes from 13:00 until <paramref name="until"/>. Den radiator 1,
    /// also shut, let nothing through and sat at 20.8 °C.
    /// </summary>
    private static void ReplayThe23rd(IValveHealthMonitor monitor, DateTimeOffset until)
    {
        for (DateTimeOffset now = At(13, 0); now <= until; now = now.AddMinutes(5))
        {
            bool boilerBelievedOn = now >= At(14, 23) && now < At(14, 37);
            monitor.Observe(Bedroom3, new ValveObservation(now < At(15, 28), ReadingAt(Bedroom3Readings, now), 21, 16.5), boilerBelievedOn, now);
            Shut(monitor, DenRadiator2, ReadingAt(Radiator2, now), boilerBelievedOn, now);
            Shut(monitor, DenRadiator1, 20.8, boilerBelievedOn, now);
        }
    }

    /// <summary>
    /// Makes <paramref name="valve"/> be seen letting water through while shut: shut and settled, it climbs 3 °C while
    /// another radiator warms. For other tests that need a valve with a finding.
    /// </summary>
    internal static void SeeALeak(IValveHealthMonitor monitor, string valve, DateTimeOffset start)
    {
        string other = string.Equals(valve, Bedroom3, StringComparison.OrdinalIgnoreCase) ? DenRadiator1 : Bedroom3;
        for (int minutes = 0; minutes <= 90; minutes += 5)
        {
            DateTimeOffset now = start.AddMinutes(minutes);
            Open(monitor, other, 17 + minutes / 15.0, true, now);
            Shut(monitor, valve, minutes < 50 ? 19 : 19 + (minutes - 50) / 10.0, true, now);
        }
    }

    [TestMethod]
    public void The23rd_FlagsDenRadiator2_AsLettingWaterThroughWhileShut()
    {
        ValveHealthMonitor monitor = Create();

        ReplayThe23rd(monitor, At(15, 35));

        ValveHealthRecord? leak = Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut);
        IsFlagged(leak).ShouldBeTrue();
        leak!.Count.ShouldBe(1);
        // Seen once it had climbed 2 °C, at the pass after the belief went off: the belief still counts for a while.
        leak.LastSeenAt.ShouldBe(At(14, 40));
        leak.LastSeenDetail.ShouldBe("Rose from 20.1 °C to 27.1 °C while shut, with hot water flowing");
        leak.FromTemperature.ShouldBe(20.1);
        leak.ToTemperature.ShouldBe(27.1, "followed to the top of the climb");
    }

    [TestMethod]
    public void The23rd_LeavesTheOtherValvesClear()
    {
        ValveHealthMonitor monitor = Create();

        ReplayThe23rd(monitor, At(16, 0));

        IsFlagged(Record(monitor, DenRadiator1, ValveHealthKinds.LeaksWhenShut)).ShouldBeFalse();
        IsFlagged(Record(monitor, Bedroom3, ValveHealthKinds.DoesNotWarm)).ShouldBeFalse();
        // Bedroom 3 warmed while open, which shows its valve works.
        Record(monitor, Bedroom3, ValveHealthKinds.DoesNotWarm)!.LastOkAt.ShouldNotBeNull();
        // Radiator 1 sat through an hour of Bedroom 3 proving hot water without warming, which shows it shuts.
        Record(monitor, DenRadiator1, ValveHealthKinds.LeaksWhenShut)!.LastOkAt.ShouldNotBeNull();
    }

    [TestMethod]
    public void AShutValveThatClimbsWithNoHotWater_IsNotFlagged()
    {
        // The sun on the radiator, or a fan heater beside it: no boiler, no other radiator warming.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 180; minutes += 5)
        {
            Shut(monitor, DenRadiator2, 18 + minutes / 30.0, false, At(10, 0).AddMinutes(minutes));
        }

        Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut).ShouldBeNull();
    }

    [TestMethod]
    public void AValveJustShut_MayCoastUpWithoutBeingFlagged()
    {
        // Water left in the radiator keeps warming it for a while after the valve closes.
        ValveHealthMonitor monitor = Create();
        Open(monitor, DenRadiator2, 20, true, At(10, 0));
        for (int minutes = 5; minutes <= 40; minutes += 5)
        {
            Shut(monitor, DenRadiator2, 20 + minutes / 10.0, true, At(10, 0).AddMinutes(minutes));
        }

        Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut).ShouldBeNull();
    }

    [TestMethod]
    public void AnOpenValveThatStaysCold_WhileAnotherRadiatorWarms_IsFlagged()
    {
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 70; minutes += 5)
        {
            DateTimeOffset now = At(6, 0).AddMinutes(minutes);
            Open(monitor, Bedroom3, 17 + minutes / 15.0, true, now);
            Open(monitor, DenRadiator1, 18.0, true, now);
        }

        ValveHealthRecord? cold = Record(monitor, DenRadiator1, ValveHealthKinds.DoesNotWarm);
        IsFlagged(cold).ShouldBeTrue();
        cold!.LastSeenAt.ShouldBe(At(7, 0));
        cold.LastSeenDetail.ShouldBe("Open for 60 minutes while another radiator warmed, but rose only 0.0 °C (from 18.0 °C)");
        cold.FromTemperature.ShouldBe(18);
        cold.ToTemperature.ShouldBe(18);
        cold.Minutes.ShouldBe(60);
    }

    [TestMethod]
    public void AnOpenValveThatStaysCold_IsNotFlagged_OnTheBeliefAlone()
    {
        // At 02:42 on 2026-10-22 the fingerbot reported a press that never reached the boiler: every radiator stayed
        // cold, so a cold one says nothing about its own valve.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 120; minutes += 5)
        {
            DateTimeOffset now = At(2, 0).AddMinutes(minutes);
            Open(monitor, Bedroom3, 17, true, now);
            Open(monitor, DenRadiator1, 18, true, now);
        }

        Record(monitor, DenRadiator1, ValveHealthKinds.DoesNotWarm).ShouldBeNull();
        Record(monitor, Bedroom3, ValveHealthKinds.DoesNotWarm).ShouldBeNull();
    }

    [TestMethod]
    public void AnOpenRadiatorAlreadyHot_IsNotHeldToRising()
    {
        // Den radiator 2 at 26.3 °C with the den at 21 °C, open while another radiator warmed: it had no further to go.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 90; minutes += 5)
        {
            DateTimeOffset now = At(4, 30).AddMinutes(minutes);
            Open(monitor, Bedroom3, 17 + minutes / 15.0, true, now);
            Open(monitor, DenRadiator2, 26.3, true, now, room: 21);
        }

        Record(monitor, DenRadiator2, ValveHealthKinds.DoesNotWarm).ShouldBeNull();
    }

    [TestMethod]
    public void AValveAtItsSetpoint_IsNotHeldToRising()
    {
        // A head at its setpoint throttles its valve nearly shut, so it is not expected to warm the radiator.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 90; minutes += 5)
        {
            DateTimeOffset now = At(6, 0).AddMinutes(minutes);
            Open(monitor, Bedroom3, 17 + minutes / 15.0, true, now);
            Open(monitor, DenRadiator1, 18, true, now, setpoint: 18.5);
        }

        Record(monitor, DenRadiator1, ValveHealthKinds.DoesNotWarm).ShouldBeNull();
    }

    [TestMethod]
    public void ALaterCleanRun_ClearsAnEarlierLeak()
    {
        // As after radiator 2's head relearnt its travel: the next boiler run, it stays put while Bedroom 3 warms.
        ValveHealthMonitor monitor = Create();
        ReplayThe23rd(monitor, At(15, 35));

        DateTimeOffset start = At(20, 0);
        for (int minutes = 0; minutes <= 120; minutes += 5)
        {
            DateTimeOffset now = start.AddMinutes(minutes);
            Open(monitor, Bedroom3, 17 + minutes / 20.0, true, now);
            Shut(monitor, DenRadiator2, 20.5, true, now);
        }

        ValveHealthRecord leak = Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut)!;
        IsFlagged(leak).ShouldBeFalse();
        leak.Count.ShouldBe(1);
        leak.LastOkAt.ShouldNotBeNull();
    }

    [TestMethod]
    public void TheBeliefAlone_NeverClearsALeak()
    {
        // A boiler believed on but not running would leave a leaking valve's radiator cold too.
        ValveHealthMonitor monitor = Create();
        ReplayThe23rd(monitor, At(15, 35));

        for (int minutes = 0; minutes <= 120; minutes += 5)
        {
            Shut(monitor, DenRadiator2, 20.5, true, At(20, 0).AddMinutes(minutes));
        }

        IsFlagged(Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut)).ShouldBeTrue();
    }

    [TestMethod]
    public void OneClimb_IsOneSighting()
    {
        ValveHealthMonitor monitor = Create();

        ReplayThe23rd(monitor, At(17, 0));

        Record(monitor, DenRadiator2, ValveHealthKinds.LeaksWhenShut)!.Count.ShouldBe(1);
    }

    [TestMethod]
    public void Findings_SurviveARestart()
    {
        InMemoryValveHealthStore store = new();
        ReplayThe23rd(Create(store), At(15, 35));

        ValveHealthMonitor restarted = Create(store);

        IsFlagged(Record(restarted, DenRadiator2, ValveHealthKinds.LeaksWhenShut)).ShouldBeTrue();
    }

    [TestMethod]
    public void TheValveExercise_StartsEveryValveAgain()
    {
        // The exercise opens every shut valve for 2½ minutes with the boiler on; the warmth that leaves behind is not a leak.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 60; minutes += 5)
        {
            Shut(monitor, DenRadiator1, 18, false, At(7, 0).AddMinutes(minutes));
        }

        monitor.ResetSpells();
        for (int minutes = 0; minutes <= 30; minutes += 5)
        {
            Shut(monitor, DenRadiator1, 18 + minutes / 6.0, true, At(8, 5).AddMinutes(minutes));
        }

        Record(monitor, DenRadiator1, ValveHealthKinds.LeaksWhenShut).ShouldBeNull();
    }

    [TestMethod]
    public void AGapInObservations_StartsTheSpellAgain()
    {
        // The app was down: what the valve did meanwhile is unknown, so it must settle again before counting.
        ValveHealthMonitor monitor = Create();
        for (int minutes = 0; minutes <= 60; minutes += 5)
        {
            Shut(monitor, DenRadiator1, 18, true, At(7, 0).AddMinutes(minutes));
        }

        Shut(monitor, DenRadiator1, 21, true, At(8, 30));

        Record(monitor, DenRadiator1, ValveHealthKinds.LeaksWhenShut).ShouldBeNull();
    }
}

internal sealed class InMemoryValveHealthStore : IValveHealthStore
{
    public Dictionary<string, Dictionary<string, ValveHealthRecord>> Saved { get; set; } = [];

    public Dictionary<string, Dictionary<string, ValveHealthRecord>> Load() =>
        Saved.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(inner => inner.Key, inner => inner.Value));

    public void Save(Dictionary<string, Dictionary<string, ValveHealthRecord>> records) =>
        Saved = records.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(inner => inner.Key, inner => inner.Value));
}
