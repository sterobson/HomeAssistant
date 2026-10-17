using HomeAssistant.apps.Energy;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// From October 2026 the import tariff is Intelligent Octopus Go 12M Fixed, for which Octopus's
/// public REST endpoint publishes no unit rates. OctopusReader therefore returns nothing, and an
/// empty list used to fall through to a rate of 0 - free electricity - in battery planning and
/// stored pricing. Home Assistant's Octopus integration does price the tariff; these tests pin
/// that its events are read correctly (shape and units verified against the live
/// event.octopus_energy_electricity_*_current_day_rates on 2026-10-17), that REST is only
/// replaced when it comes back empty, that export is untouched, and that the "nothing at all"
/// case is reported instead of silent.
/// </summary>
[TestClass]
public sealed class ImportRatesFallbackTests
{
    private const string ImportPrefix = "event.octopus_energy_electricity_24j0494691_1591015382045";
    private const string ExportPrefix = "event.octopus_energy_electricity_24j0494691_1574300576548_export";

    // 2026-10-17 is BST (UTC+1); the real events carry +01:00 offsets.
    private static readonly DateTime DayStartUtc = new(2026, 10, 16, 23, 0, 0, DateTimeKind.Utc);

    // ---- Parsing the HA event ----

    [TestMethod]
    public void ParseRates_ConvertsPoundsToPenceAndOffsetsToUtc()
    {
        JsonElement attributes = EventAttributes(
            ("2026-10-17T05:00:00+01:00", "2026-10-17T05:30:00+01:00", 0.076),
            ("2026-10-17T05:30:00+01:00", "2026-10-17T06:00:00+01:00", 0.356164));

        List<EnergyRate> rates = HomeAssistantImportRates.ParseRates(attributes);

        rates.Count.ShouldBe(2);
        rates[0].StartTimeUtc.ShouldBe(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc));
        rates[0].EndTimeUtc.ShouldBe(new DateTime(2026, 10, 17, 4, 30, 0, DateTimeKind.Utc));
        rates[0].RateIncVat.ShouldBe(7.6, 0.0001);
        rates[1].RateIncVat.ShouldBe(35.6164, 0.0001);
        rates[1].RateExcVat.ShouldBe(35.6164 / 1.05, 0.0001);
    }

    [TestMethod]
    public void ParseRates_SkipsMalformedSlotsAndToleratesMissingAttribute()
    {
        HomeAssistantImportRates.ParseRates(JsonDocument.Parse("""{"friendly_name":"x"}""").RootElement)
            .ShouldBeEmpty();

        JsonElement attributes = JsonDocument.Parse("""
            {"rates":[
              {"start":"not a date","end":"2026-10-17T00:30:00+01:00","value_inc_vat":0.1},
              {"start":"2026-10-17T00:30:00+01:00","end":"2026-10-17T01:00:00+01:00"},
              {"start":"2026-10-17T01:00:00+01:00","end":"2026-10-17T01:30:00+01:00","value_inc_vat":0.2}
            ]}
            """).RootElement;

        List<EnergyRate> rates = HomeAssistantImportRates.ParseRates(attributes);

        rates.Count.ShouldBe(1);
        rates[0].RateIncVat.ShouldBe(20, 0.0001);
    }

    [TestMethod]
    public void FindImportMeterPrefix_PicksTheImportMeterAndNeverTheExportOrGasOnes()
    {
        string?[] ids =
        [
            $"{ExportPrefix}_current_day_rates",
            "event.octopus_energy_gas_e6s12951182562_1902909205_current_day_rates",
            $"{ImportPrefix}_current_day_rates",
            $"{ImportPrefix}_next_day_rates"
        ];

        HomeAssistantImportRates.FindImportMeterPrefix(ids!).ShouldBe(ImportPrefix);
    }

    [TestMethod]
    public void FindImportMeterPrefix_ReturnsNullWhenIntegrationIsAbsent()
    {
        HomeAssistantImportRates.FindImportMeterPrefix(["sensor.something", $"{ExportPrefix}_current_day_rates"])
            .ShouldBeNull();
    }

    // ---- The HA-backed source, through IHaContext ----

    [TestMethod]
    public void HomeAssistantSource_StitchesPreviousCurrentAndNextDayAndFiltersToTheRange()
    {
        IHaContext ha = HaWith(
            (ImportPrefix + "_previous_day_rates", SlotsForDay(new DateTime(2026, 10, 16, 0, 0, 0), 0.05)),
            (ImportPrefix + "_current_day_rates", SlotsForDay(new DateTime(2026, 10, 17, 0, 0, 0), 0.30)),
            (ImportPrefix + "_next_day_rates", SlotsForDay(new DateTime(2026, 10, 18, 0, 0, 0), 0.40)),
            (ExportPrefix + "_current_day_rates", SlotsForDay(new DateTime(2026, 10, 17, 0, 0, 0), 0.12)));
        HomeAssistantImportRates sut = new(ha);

        // Local day 17 Oct (BST) = 16 Oct 23:00 UTC to 17 Oct 23:00 UTC.
        List<EnergyRate> today = sut.GetImportRates(DayStartUtc, DayStartUtc.AddDays(1));

        today.Count.ShouldBe(48);
        today.ShouldAllBe(r => Math.Abs(r.RateIncVat - 30) < 0.0001, "export's 12p and the other days' rates must not leak in");
        today[0].StartTimeUtc.ShouldBe(DayStartUtc);

        sut.GetImportRates(DayStartUtc.AddDays(1), DayStartUtc.AddDays(2)).Count.ShouldBe(48);
        sut.GetImportRates(DayStartUtc.AddDays(-1), DayStartUtc.AddDays(2)).Count.ShouldBe(144);
    }

    [TestMethod]
    public void HomeAssistantSource_CurrentRateIsTheSlotCoveringNow()
    {
        IHaContext ha = HaWith(
            (ImportPrefix + "_current_day_rates", EventAttributes(
                ("2026-10-17T05:00:00+01:00", "2026-10-17T05:30:00+01:00", 0.076),
                ("2026-10-17T05:30:00+01:00", "2026-10-17T06:00:00+01:00", 0.356164))));
        HomeAssistantImportRates sut = new(ha);

        EnergyRate? atFivePastFive = sut.GetCurrentImportRate(new DateTime(2026, 10, 17, 4, 5, 0, DateTimeKind.Utc));
        EnergyRate? atSixTen = sut.GetCurrentImportRate(new DateTime(2026, 10, 17, 5, 10, 0, DateTimeKind.Utc));
        EnergyRate? beforeAnyData = sut.GetCurrentImportRate(new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc));

        atFivePastFive.ShouldNotBeNull().RateIncVat.ShouldBe(7.6, 0.0001);
        atSixTen.ShouldBeNull("05:10 UTC is 06:10 BST, which is past the last slot supplied");
        beforeAnyData.ShouldBeNull();
    }

    [TestMethod]
    public void HomeAssistantSource_ReturnsNothingWhenTheIntegrationHasNoEvents()
    {
        HomeAssistantImportRates sut = new(HaWith());

        sut.GetImportRates(DayStartUtc, DayStartUtc.AddDays(1)).ShouldBeEmpty();
        sut.GetCurrentImportRate(DayStartUtc).ShouldBeNull();
    }

    // ---- The fallback composite ----

    [TestMethod]
    public async Task Import_UsesRestWhenItHasRatesAndNeverConsultsHomeAssistant()
    {
        Fixture f = new();
        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns([RateOf(25.1214)]);

        List<EnergyRate> rates = await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1));

        rates.Single().RateIncVat.ShouldBe(25.1214, 0.0001);
        f.HomeAssistant.DidNotReceiveWithAnyArgs().GetImportRates(default, default);
        f.Logger.Warnings.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Import_FallsBackToHomeAssistantWhenRestIsEmpty()
    {
        Fixture f = new();
        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        f.HomeAssistant.GetImportRates(DayStartUtc, DayStartUtc.AddDays(1)).Returns([RateOf(35.6164)]);

        List<EnergyRate> rates = await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1));

        rates.Single().RateIncVat.ShouldBe(35.6164, 0.0001);
        f.Logger.Warnings.ShouldBeEmpty("a working fallback is not a fault");
    }

    [TestMethod]
    public async Task CurrentImport_FallsBackToHomeAssistantWhenRestReturnsTheDefaultRate()
    {
        Fixture f = new();
        // OctopusReader's "nothing matched" result is a default-constructed rate.
        f.Primary.GetCurrentElectricityImportRateAsync().Returns(new EnergyRate());
        f.Time.SetSpecificDateTime(new DateTimeOffset(2026, 10, 17, 17, 0, 0, TimeSpan.Zero));
        f.HomeAssistant.GetCurrentImportRate(f.Time.GetUtcNow().UtcDateTime).Returns(RateOf(35.6164));

        EnergyRate rate = await f.Sut.GetCurrentElectricityImportRateAsync();

        rate.RateIncVat.ShouldBe(35.6164, 0.0001);
    }

    [TestMethod]
    public async Task Import_WarnsExactlyOnceWhenNeitherSourceHasRates()
    {
        Fixture f = new();
        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        f.HomeAssistant.GetImportRates(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);

        for (int i = 0; i < 5; i++)
        {
            (await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1))).ShouldBeEmpty();
        }

        f.Logger.Warnings.Count.ShouldBe(1);
        f.Logger.Warnings[0].ShouldContain("import rates");
        f.Logger.Warnings[0].ShouldContain("0p/kWh");
    }

    [TestMethod]
    public async Task Import_WarnsAgainIfRatesComeBackAndThenGoAgain()
    {
        Fixture f = new();
        f.HomeAssistant.GetImportRates(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1));

        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([RateOf(10)]);
        await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1));

        f.Primary.GetElectricityImportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        await f.Sut.GetElectricityImportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1));

        f.Logger.Warnings.Count.ShouldBe(2);
    }

    [TestMethod]
    public async Task CurrentImport_WarnsAndReturnsTheDefaultWhenNeitherSourceHasARate()
    {
        Fixture f = new();
        f.Primary.GetCurrentElectricityImportRateAsync().Returns(new EnergyRate());
        f.HomeAssistant.GetCurrentImportRate(Arg.Any<DateTime>()).Returns((EnergyRate?)null);

        EnergyRate rate = await f.Sut.GetCurrentElectricityImportRateAsync();

        rate.RateIncVat.ShouldBe(0);
        f.Logger.Warnings.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Export_IsPassedStraightThroughAndNeverFallsBack()
    {
        Fixture f = new();
        f.Primary.GetElectricityExportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([RateOf(12)]);
        f.Primary.GetCurrentElectricityExportRateAsync().Returns(RateOf(12));

        (await f.Sut.GetElectricityExportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1))).Single().RateIncVat.ShouldBe(12);
        (await f.Sut.GetCurrentElectricityExportRateAsync()).RateIncVat.ShouldBe(12);

        // And an empty export result is not this class's business: no HA call, no import warning.
        f.Primary.GetElectricityExportRatesAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);
        (await f.Sut.GetElectricityExportRatesAsync(DayStartUtc, DayStartUtc.AddDays(1))).ShouldBeEmpty();

        f.HomeAssistant.ReceivedCalls().ShouldBeEmpty();
        f.Logger.Warnings.ShouldBeEmpty();
    }

    // ---- Helpers ----

    private sealed class Fixture
    {
        public IElectricityRatesReader Primary { get; } = Substitute.For<IElectricityRatesReader>();
        public IHomeAssistantImportRates HomeAssistant { get; } = Substitute.For<IHomeAssistantImportRates>();
        public FakeTimeProvider Time { get; } = new();
        public CapturingLogger<FallbackElectricityRatesReader> Logger { get; } = new();
        public FallbackElectricityRatesReader Sut { get; }

        public Fixture()
        {
            Sut = new FallbackElectricityRatesReader(Primary, HomeAssistant, Time, Logger);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private static EnergyRate RateOf(double pencePerKwh) => new()
    {
        StartTimeUtc = DayStartUtc,
        EndTimeUtc = DayStartUtc.AddMinutes(30),
        RateIncVat = pencePerKwh
    };

    /// <summary>A day of 48 half-hourly slots starting at local (BST) midnight of <paramref name="localDay"/>.</summary>
    private static JsonElement SlotsForDay(DateTime localDay, double poundsPerKwh)
    {
        List<(string, string, double)> slots = [];
        for (int i = 0; i < 48; i++)
        {
            DateTime start = localDay.AddMinutes(30 * i);
            slots.Add((Local(start), Local(start.AddMinutes(30)), poundsPerKwh));
        }

        return EventAttributes([.. slots]);
    }

    private static string Local(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss") + "+01:00";

    private static JsonElement EventAttributes(params (string Start, string End, double Value)[] slots)
    {
        var payload = new
        {
            rates = slots.Select(s => new { start = s.Start, end = s.End, value_inc_vat = s.Value, is_capped = false }),
            tariff_code = "E-1R-IOG-SMB-FIX-12M-26-10-01-F"
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    private static IHaContext HaWith(params (string EntityId, JsonElement Attributes)[] entities)
    {
        IHaContext ha = Substitute.For<IHaContext>();
        ha.GetState(Arg.Any<string>()).Returns((EntityState?)null);

        List<Entity> all = [];
        foreach ((string entityId, JsonElement attributes) in entities)
        {
            ha.GetState(entityId).Returns(new EntityState { EntityId = entityId, AttributesJson = attributes });
            all.Add(new Entity(ha, entityId));
        }

        ha.GetAllEntities().Returns(all);
        return ha;
    }
}
