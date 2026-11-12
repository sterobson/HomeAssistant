using HomeAssistant.apps;
using HomeAssistant.apps.Energy;
using HomeAssistant.Services;
using HomeAssistant.Services.Energy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// On 2026-11-10 at 17:31 UTC Home Assistant restarted uncleanly, the Octopus integration failed to start
/// (HOME-ASSISTANT.md gotcha 36) and its rate sensors read <c>unavailable</c> for two days. The energy history read
/// them as numbers, so <c>unavailable</c> became a rate of 0p/kWh: every hour in between was stored with no cost, and
/// the day's battery price slots gained a 0p slot. These tests pin that a stretch with no numeric rate is filled from
/// Octopus (pence there, pounds here), and that a stretch Octopus cannot fill is reported rather than priced at 0.
/// </summary>
[TestClass]
public sealed class EnergyRateHistoryReaderTests
{
    private const string Sensor = "sensor.octopus_energy_electricity_current_rate";

    private static readonly DateTime Midnight = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int hour, int minute = 0) => Midnight.AddHours(hour).AddMinutes(minute);

    // ---- A sensor that was working ----

    [TestMethod]
    public async Task NumericHistory_IsKeptAsItWasAndOctopusIsNotAsked()
    {
        Fixture f = new();
        f.Sensor(("0.356164", At(5, 30)), ("0.076", At(23, 30)));

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(6), At(24));

        Pairs(sut.Entries).ShouldBe([(At(6), 0.356164), (At(23, 30), 0.076)]);
        sut.Unpriced.ShouldBeEmpty();
        await f.Rates.DidNotReceiveWithAnyArgs().GetElectricityImportRatesAsync(default, default);
        await f.Rates.DidNotReceiveWithAnyArgs().GetElectricityExportRatesAsync(default, default);
    }

    // ---- A sensor that went unavailable ----

    [TestMethod]
    public async Task AnUnavailableStretch_IsFilledFromOctopus_InPoundsNotPence()
    {
        Fixture f = new();
        f.Sensor(("0.356164", At(5, 30)), ("unavailable", At(17, 33)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(
        [
            Rate(At(5, 30), At(23, 30), 35.6164),
            Rate(At(23, 30), At(29, 30), 7.6)
        ]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(17), At(24));

        Pairs(sut.Entries).ShouldBe(
        [
            (At(17), 0.356164),
            (At(17, 33), 0.356164),
            (At(23, 30), 0.076)
        ]);
        sut.Entries.ShouldAllBe(e => e.State > 0);
        sut.Unpriced.ShouldBeEmpty();
        sut.IsPricedThroughout(At(17), At(24)).ShouldBeTrue();
    }

    [TestMethod]
    public async Task ExportAsksOctopusForExportRates()
    {
        Fixture f = new();
        f.Sensor(("unavailable", At(0)));
        f.Rates.GetElectricityExportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns([Rate(At(0).AddDays(-3), DateTime.MaxValue, 12)]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: true, At(0), At(24));

        Pairs(sut.Entries).ShouldBe([(At(0), 0.12)]);
        await f.Rates.DidNotReceiveWithAnyArgs().GetElectricityImportRatesAsync(default, default);
    }

    [TestMethod]
    public async Task ASensorWithNoHistoryAtAll_IsPricedFromOctopus()
    {
        Fixture f = new();
        f.Sensor();
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns([Rate(At(0).AddDays(-1), DateTime.MaxValue, 35.6164)]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(0), At(24));

        Pairs(sut.Entries).ShouldBe([(At(0), 0.356164)]);
        sut.Unpriced.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ARateThatOnlyStartsPartWayThroughTheGap_LeavesTheEarlierPartUnpriced()
    {
        Fixture f = new();
        f.Sensor(("unavailable", At(0)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns([Rate(At(12), At(24), 35.6164)]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(0), At(24));

        sut.Unpriced.ShouldBe([(At(0), At(12))]);
        sut.IsPricedThroughout(At(13), At(14)).ShouldBeTrue();
        sut.IsPricedThroughout(At(11), At(12)).ShouldBeFalse();
    }

    // ---- Neither Home Assistant nor Octopus has a rate ----

    [TestMethod]
    public async Task WhenOctopusHasNothing_TheGapIsReportedNotPricedAtZero()
    {
        Fixture f = new();
        f.Sensor(("0.356164", At(5, 30)), ("unavailable", At(17, 33)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(17), At(24));

        sut.Entries.ShouldNotContain(e => e.State == 0);
        sut.Unpriced.ShouldBe([(At(17, 33), At(24))]);
        sut.IsPricedThroughout(At(17), At(18)).ShouldBeFalse();
        sut.IsPricedThroughout(At(17), At(17, 33)).ShouldBeTrue();
    }

    [TestMethod]
    public async Task AnEmptyRateFromOctopus_IsNotMistakenForARate()
    {
        // OctopusReader returns a default EnergyRate (end time unset) when nothing matches.
        Fixture f = new();
        f.Sensor(("unavailable", At(0)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([new EnergyRate()]);

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(0), At(24));

        sut.Entries.ShouldBeEmpty();
        sut.Unpriced.ShouldBe([(At(0), At(24))]);
    }

    [TestMethod]
    public async Task WhenOctopusCannotBeReached_TheGapIsReportedAndNothingThrows()
    {
        Fixture f = new();
        f.Sensor(("unavailable", At(0)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns<List<EnergyRate>>(_ => throw new HttpRequestException("no route to host"));

        EnergyRateHistory sut = await f.Reader.GetAsync(Sensor, isExport: false, At(0), At(24));

        sut.Entries.ShouldBeEmpty();
        sut.Unpriced.ShouldBe([(At(0), At(24))]);
    }

    [TestMethod]
    public void AnUnpricedHour_IsOnlyAProblemIfEnergyMovedInIt()
    {
        EnergyRateHistory unpriced = new() { Unpriced = [(At(19), At(20))] };

        EnergyHistoryPushService.HasUnpricedEnergy(unpriced, 0.111, At(19), At(20)).ShouldBeTrue();
        EnergyHistoryPushService.HasUnpricedEnergy(unpriced, -0.111, At(19), At(20)).ShouldBeTrue();
        EnergyHistoryPushService.HasUnpricedEnergy(unpriced, 0, At(19), At(20)).ShouldBeFalse();
        EnergyHistoryPushService.HasUnpricedEnergy(unpriced, 0.111, At(20), At(21)).ShouldBeFalse();
        EnergyHistoryPushService.HasUnpricedEnergy(null, 0.111, At(19), At(20)).ShouldBeFalse();
    }

    // ---- The cost that comes out ----

    [TestMethod]
    public async Task AnHourAcrossTheNightRateSwitch_IsCostedAtBothRates_WhileTheSensorIsUnavailable()
    {
        // The 23:00 hour on 2026-11-10: 2 kW drawn throughout, the sensor down, the rate changing at 23:30.
        // Half an hour at 35.6164p and half at 7.6p: 2 kW x 0.5 h x 35.6164p + 2 kW x 0.5 h x 7.6p = 43.2164p.
        // Before the fix this came out at 0p.
        Fixture f = new();
        f.Sensor(("unavailable", At(17, 33)));
        f.Rates.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(
        [
            Rate(At(5, 30), At(23, 30), 35.6164),
            Rate(At(23, 30), At(29, 30), 7.6)
        ]);

        EnergyRateHistory rates = await f.Reader.GetAsync(Sensor, isExport: false, At(23), At(24));
        List<NumericHistoryEntry> power = [new() { LastChanged = At(23), State = 2000 }];

        double pence = EnergyHistoryPushService.IntegrateCostPence(power, rates.Entries, At(23), At(24));

        pence.ShouldBe(43.2164, 0.0001);
    }

    // ---- Helpers ----

    // 35.6164 / 100 is not bit-for-bit 0.356164, so compare to the six decimal places a rate is quoted to.
    private static List<(DateTime LastChanged, double PoundsPerKwh)> Pairs(IReadOnlyList<NumericHistoryEntry> entries) =>
        [.. entries.Select(e => (e.LastChanged, Math.Round(e.State, 6)))];

    private static EnergyRate Rate(DateTime from, DateTime to, double pence) =>
        new() { StartTimeUtc = from, EndTimeUtc = to, RateIncVat = pence, RateExcVat = pence / 1.05 };

    private sealed class Fixture
    {
        public IElectricityRatesReader Rates { get; } = Substitute.For<IElectricityRatesReader>();
        public ScriptedRateHistory History { get; } = new();
        public EnergyRateHistoryReader Reader { get; }

        public Fixture()
        {
            Reader = new EnergyRateHistoryReader(History, Rates, NullLogger<EnergyRateHistoryReader>.Instance);
        }

        public void Sensor(params (string State, DateTime LastChanged)[] states)
        {
            History.States = [.. states.Select(s => new HistoryTextEntry { State = s.State, LastChanged = s.LastChanged })];
        }
    }

    /// <summary>
    /// What the recorder returns for the sensor, cut to the window asked for the way Home Assistant does: the state the
    /// window opens with (stamped at the window's start) and every change inside it.
    /// </summary>
    private sealed class ScriptedRateHistory()
        : HistoryService(new HomeAssistantConfiguration(), Substitute.For<ILogger<HistoryService>>())
    {
        public List<HistoryTextEntry> States { get; set; } = [];

        public override Task<IReadOnlyList<HistoryTextEntry>> GetEntityTextHistory(string entityId, DateTime from, DateTime to)
        {
            List<HistoryTextEntry> result = [];
            HistoryTextEntry? opening = States.LastOrDefault(s => s.LastChanged <= from);
            if (opening != null)
            {
                result.Add(new HistoryTextEntry { State = opening.State, LastChanged = from });
            }

            result.AddRange(States.Where(s => s.LastChanged > from && s.LastChanged < to));
            return Task.FromResult<IReadOnlyList<HistoryTextEntry>>(result);
        }
    }
}
