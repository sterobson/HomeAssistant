using HomeAssistant.Services.Climate;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public class WarmUpRatesTests
{
    private const string Valve = "climate.bedroom_3_radiator_thermostat";
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => Start.AddMinutes(minutes);

    private static WarmUpRates Create(InMemoryWarmUpRateStore? store = null) => new(store ?? new InMemoryWarmUpRateStore());

    /// <summary>One heating run: observed every 15 minutes, the room rising by <paramref name="degreesPerHour"/> from <paramref name="from"/>, until it reaches <paramref name="target"/>.</summary>
    private static void HeatToTarget(WarmUpRates rates, DateTimeOffset startsAt, double from, double target, double degreesPerHour) =>
        HeatToTarget(rates, Valve, startsAt, from, target, degreesPerHour);

    /// <summary>The same for any valve, for other tests that need one to have a measured rate.</summary>
    internal static void HeatToTarget(IWarmUpRates rates, string valve, DateTimeOffset startsAt, double from, double target, double degreesPerHour)
    {
        double temperature = from;
        int minutes = 0;
        while (true)
        {
            rates.Observe(valve, true, temperature, target, startsAt.AddMinutes(minutes));
            if (temperature >= target)
            {
                break;
            }

            minutes += 15;
            temperature = Math.Min(target, from + degreesPerHour * minutes / 60);
        }

        // The valve then shuts, which ends the run.
        rates.Observe(valve, false, target, target, startsAt.AddMinutes(minutes + 15));
    }

    [TestMethod]
    public void ARunThatReachesItsTarget_GivesItsRate()
    {
        WarmUpRates rates = Create();

        HeatToTarget(rates, At(0), from: 17, target: 20, degreesPerHour: 2);

        rates.RateFor(Valve).ShouldNotBeNull().ShouldBe(2, 0.001);
        rates.SampleCount(Valve).ShouldBe(1);
    }

    [TestMethod]
    public void ARunUnderHalfAnHour_IsIgnored()
    {
        WarmUpRates rates = Create();

        HeatToTarget(rates, At(0), from: 19.5, target: 20, degreesPerHour: 2); // 15 minutes

        rates.RateFor(Valve).ShouldBeNull();
    }

    [TestMethod]
    public void ARunOfExactlyHalfAnHour_Counts()
    {
        WarmUpRates rates = Create();

        HeatToTarget(rates, At(0), from: 19, target: 20, degreesPerHour: 2); // 30 minutes

        rates.SampleCount(Valve).ShouldBe(1);
    }

    [TestMethod]
    public void ARunThatEndsBeforeItsTarget_IsIgnored()
    {
        // The boiler went off (or the valve shut) with the room still short.
        WarmUpRates rates = Create();
        for (int minutes = 0; minutes <= 60; minutes += 15)
        {
            rates.Observe(Valve, true, 17 + 1.5 * minutes / 60, 20, At(minutes));
        }

        rates.Observe(Valve, false, 18.5, 20, At(75));

        rates.RateFor(Valve).ShouldBeNull();
    }

    [TestMethod]
    public void ARunWhoseTargetMoves_IsIgnored()
    {
        WarmUpRates rates = Create();
        rates.Observe(Valve, true, 17, 19, At(0));
        rates.Observe(Valve, true, 17.5, 19, At(15));
        rates.Observe(Valve, true, 18, 19, At(30));
        rates.Observe(Valve, true, 18.5, 19, At(45));
        rates.Observe(Valve, true, 19, 21, At(60)); // a warmer track took over; the room is at the old target, not the new

        rates.RateFor(Valve).ShouldBeNull();
    }

    [TestMethod]
    public void OnlyThePeriodToTheTarget_IsMeasured()
    {
        // Reached 20 °C after an hour (3 degrees: 3 °C an hour); the valve stays open to 20.2, which is not part of it.
        WarmUpRates rates = Create();
        for (int minutes = 0; minutes <= 60; minutes += 15)
        {
            rates.Observe(Valve, true, 17 + 3.0 * minutes / 60, 20, At(minutes));
        }

        rates.Observe(Valve, true, 20.2, 20, At(75));
        rates.Observe(Valve, false, 20.2, 20, At(90));

        rates.RateFor(Valve).ShouldNotBeNull().ShouldBe(3, 0.001);
        rates.SampleCount(Valve).ShouldBe(1, "one heating run gives one sample");
    }

    [TestMethod]
    public void ARoomAlreadyAtItsTarget_IsNotBeingWarmed()
    {
        WarmUpRates rates = Create();
        rates.Observe(Valve, true, 20, 20, At(0));
        rates.Observe(Valve, true, 20, 20, At(45));

        rates.RateFor(Valve).ShouldBeNull();
    }

    [TestMethod]
    public void AGapInWhatTheAppSaw_DropsTheRun()
    {
        // 25 minutes without an observation: the app was not watching, so what happened between is unknown.
        WarmUpRates rates = Create();
        rates.Observe(Valve, true, 17, 20, At(0));
        rates.Observe(Valve, true, 20, 20, At(25 + 40));

        rates.RateFor(Valve).ShouldBeNull();
    }

    [TestMethod]
    public void TheRateIsTheAverageOfTheLastFiveRuns()
    {
        WarmUpRates rates = Create();
        double[] runRates = [1, 1, 1, 1, 1, 1, 2, 3, 4, 6]; // only the last five (1, 2, 3, 4, 6) count: 3.2
        for (int i = 0; i < runRates.Length; i++)
        {
            HeatToTarget(rates, At(i * 600), from: 14, target: 17, degreesPerHour: runRates[i]);
        }

        rates.SampleCount(Valve).ShouldBe(5);
        rates.RateFor(Valve).ShouldNotBeNull().ShouldBe(3.2, 0.001);
    }

    [TestMethod]
    public void EachValveIsMeasuredOnItsOwn()
    {
        WarmUpRates rates = Create();
        HeatToTarget(rates, At(0), from: 17, target: 20, degreesPerHour: 2);

        rates.RateFor("climate.den_radiator_1_thermostat").ShouldBeNull();
    }

    [TestMethod]
    public void TheMinutesToRise_AreWorkedOutFromTheRate()
    {
        WarmUpRates rates = Create();
        HeatToTarget(rates, At(0), from: 17, target: 20, degreesPerHour: 2);

        rates.MinutesToRise(Valve, 3).ShouldBe(90);
        rates.MinutesToRise(Valve, 0.5).ShouldBe(15);
        rates.MinutesToRise(Valve, 0).ShouldBe(0);
        rates.MinutesToRise("climate.unmeasured", 3).ShouldBeNull();
    }

    [TestMethod]
    public void TheSamplesSurviveARestart()
    {
        InMemoryWarmUpRateStore store = new();
        HeatToTarget(Create(store), At(0), from: 17, target: 20, degreesPerHour: 2);

        WarmUpRates afterRestart = Create(store);

        afterRestart.RateFor(Valve).ShouldNotBeNull().ShouldBe(2, 0.001);
        afterRestart.SampleCount(Valve).ShouldBe(1);
    }
}

internal sealed class InMemoryWarmUpRateStore : IWarmUpRateStore
{
    public Dictionary<string, List<WarmUpSample>> Saved { get; private set; } = [];

    public Dictionary<string, List<WarmUpSample>> Load() => Saved.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());

    public void Save(Dictionary<string, List<WarmUpSample>> samples) => Saved = samples;

    /// <summary>Starts a valve off with measured warm-ups at the given rates, as if it had been heating for a while.</summary>
    public InMemoryWarmUpRateStore With(string entityId, params double[] degreesPerHour)
    {
        Saved[entityId] = [.. degreesPerHour.Select(rate => new WarmUpSample(rate, DateTimeOffset.UnixEpoch, 60, 15, 15 + rate))];
        return this;
    }
}
