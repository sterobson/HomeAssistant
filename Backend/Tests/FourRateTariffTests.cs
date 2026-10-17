using HomeAssistant.apps.Energy;
using HomeAssistant.apps.Energy.Octopus;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using System.Net;
using System.Net.Http;
using System.Text;

namespace HomeAssistant.Tests;

/// <summary>
/// From 9 October 2026 the import tariff is Intelligent Octopus Go 12M Fixed
/// (<c>E-1R-IOG-SMB-FIX-12M-26-10-01-F</c>), a four-rate EV tariff. Octopus no longer publishes
/// <c>standard-unit-rates</c> for it; instead there is one open-ended record at
/// <c>day-unit-rates</c> (35.6164p inc VAT) and one at <c>night-unit-rates</c> (7.6p), and nothing in
/// the API says when the night rate applies - only the product description does (23:30-05:30
/// every night, local time). The JSON below is modelled on the real responses, verified against the
/// public API on 2026-10-17; the legacy INTELLI-FIX path must keep working unchanged.
/// </summary>
[TestClass]
public sealed class FourRateTariffTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private const double Day = 35.6164;
    private const double Night = 7.6;

    // ---- Synthesis: boundaries, BST and GMT ----

    [TestMethod]
    public void Synthesise_BstDate_PlacesNightWindowAt2230To0430Utc()
    {
        // 2026-10-17 is BST (UTC+1), so local 00:00 is 23:00Z the day before.
        DateTime from = Utc(2026, 10, 16, 23, 0);

        List<OctopusTariffRate> rates = Synthesise(from, from.AddDays(1));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 10, 16, 23, 0), Utc(2026, 10, 17, 4, 30), Night),   // 00:00-05:30 BST
            (Utc(2026, 10, 17, 4, 30), Utc(2026, 10, 17, 22, 30), Day),    // 05:30-23:30 BST
            (Utc(2026, 10, 17, 22, 30), Utc(2026, 10, 17, 23, 0), Night)   // 23:30-00:00 BST
        ]);
    }

    [TestMethod]
    public void Synthesise_GmtDate_PlacesNightWindowAt2330To0530Utc()
    {
        DateTime from = Utc(2026, 12, 1, 0, 0);

        List<OctopusTariffRate> rates = Synthesise(from, from.AddDays(1));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 1, 0, 0), Utc(2026, 12, 1, 5, 30), Night),
            (Utc(2026, 12, 1, 5, 30), Utc(2026, 12, 1, 23, 30), Day),
            (Utc(2026, 12, 1, 23, 30), Utc(2026, 12, 2, 0, 0), Night)
        ]);
    }

    [TestMethod]
    public void Synthesise_At0530Boundary_NightEndsAndDayStartsExactlyThere()
    {
        // 05:30 is the first instant of the day rate; one minute earlier is still night.
        List<OctopusTariffRate> rates = Synthesise(Utc(2026, 12, 1, 5, 29), Utc(2026, 12, 1, 5, 31));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 1, 5, 29), Utc(2026, 12, 1, 5, 30), Night),
            (Utc(2026, 12, 1, 5, 30), Utc(2026, 12, 1, 5, 31), Day)
        ]);
    }

    [TestMethod]
    public void Synthesise_At2330Boundary_DayEndsAndNightStartsExactlyThere()
    {
        List<OctopusTariffRate> rates = Synthesise(Utc(2026, 12, 1, 23, 29), Utc(2026, 12, 1, 23, 31));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 1, 23, 29), Utc(2026, 12, 1, 23, 30), Day),
            (Utc(2026, 12, 1, 23, 30), Utc(2026, 12, 1, 23, 31), Night)
        ]);
    }

    [TestMethod]
    public void Synthesise_WindowSpanningMidnight_KeepsTheNightRateAsOneRecord()
    {
        // 22:00 Dec 1 to 07:00 Dec 2 (GMT): the night window starts before midnight and ends after it.
        List<OctopusTariffRate> rates = Synthesise(Utc(2026, 12, 1, 22, 0), Utc(2026, 12, 2, 7, 0));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 1, 22, 0), Utc(2026, 12, 1, 23, 30), Day),
            (Utc(2026, 12, 1, 23, 30), Utc(2026, 12, 2, 5, 30), Night),
            (Utc(2026, 12, 2, 5, 30), Utc(2026, 12, 2, 7, 0), Day)
        ]);
    }

    [TestMethod]
    public void Synthesise_WindowStartingInsideTheNight_StartsOnTheNightRate()
    {
        // The cache starts "now minus a day", so it routinely begins mid-window.
        List<OctopusTariffRate> rates = Synthesise(Utc(2026, 12, 2, 2, 17), Utc(2026, 12, 2, 8, 0));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 2, 2, 17), Utc(2026, 12, 2, 5, 30), Night),
            (Utc(2026, 12, 2, 5, 30), Utc(2026, 12, 2, 8, 0), Day)
        ]);
    }

    [TestMethod]
    public void Synthesise_AcrossTheClocksGoingBack_NightWindowFollowsLocalTime()
    {
        // BST ends 2026-10-25 at 02:00 BST (01:00Z). The night of the 24th/25th runs 23:30 BST (22:30Z)
        // to 05:30 GMT (05:30Z) - seven hours of wall-clock-defined cheap rate, not six.
        List<OctopusTariffRate> rates = Synthesise(Utc(2026, 10, 24, 12, 0), Utc(2026, 10, 25, 12, 0));

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 10, 24, 12, 0), Utc(2026, 10, 24, 22, 30), Day),
            (Utc(2026, 10, 24, 22, 30), Utc(2026, 10, 25, 5, 30), Night),
            (Utc(2026, 10, 25, 5, 30), Utc(2026, 10, 25, 12, 0), Day)
        ]);
    }

    [TestMethod]
    public void Synthesise_ADayRateThatChangesMidWindow_IsSplitAtTheChange()
    {
        // Rates are normally one open-ended record, but if Octopus re-prices, both are returned.
        List<OctopusTariffRate> day =
        [
            RateRecord(25.1214, validFrom: Utc(2026, 7, 5, 0, 0), validTo: Utc(2026, 12, 1, 12, 0)),
            RateRecord(Day, validFrom: Utc(2026, 12, 1, 12, 0), validTo: null)
        ];

        List<OctopusTariffRate> rates = FourRateTariff.Synthesise(
            day, [RateRecord(Night, Utc(2026, 7, 5, 0, 0), null)], Utc(2026, 12, 1, 6, 0), Utc(2026, 12, 1, 18, 0), London);

        Summarise(rates).ShouldBe(
        [
            (Utc(2026, 12, 1, 6, 0), Utc(2026, 12, 1, 12, 0), 25.1214),
            (Utc(2026, 12, 1, 12, 0), Utc(2026, 12, 1, 18, 0), Day)
        ]);
    }

    [TestMethod]
    public void Synthesise_NoNightRecord_OmitsTheNightStretchRatherThanInventingARate()
    {
        List<OctopusTariffRate> rates = FourRateTariff.Synthesise(
            [RateRecord(Day, Utc(2026, 7, 5, 0, 0), null)], [], Utc(2026, 12, 1, 0, 0), Utc(2026, 12, 2, 0, 0), London);

        Summarise(rates).ShouldBe([(Utc(2026, 12, 1, 5, 30), Utc(2026, 12, 1, 23, 30), Day)]);
    }

    // ---- OctopusReader: which endpoints it asks for ----

    [TestMethod]
    public async Task OctopusReader_FourRateTariff_PricesFromDayAndNightRates()
    {
        StubOctopus octopus = StubOctopus.FourRate();
        OctopusReader sut = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> rates = await sut.GetElectricityImportRatesAsync(now.AddHours(-1), now.AddHours(25));

        rates.Select(r => r.RateIncVat).Distinct().OrderBy(r => r).ToList().ShouldBe([Night, Day]);
        rates.ShouldAllBe(r => r.EndTimeUtc > r.StartTimeUtc && r.EndTimeUtc != DateTime.MaxValue);
        octopus.Requested("/E-1R-IOG-SMB-FIX-12M-26-10-01-F/standard-unit-rates/").ShouldBeTrue();
        octopus.Requested("/products/IOG-SMB-FIX-12M-26-10-01/electricity-tariffs/E-1R-IOG-SMB-FIX-12M-26-10-01-F/day-unit-rates/").ShouldBeTrue();
        octopus.Requested("/products/IOG-SMB-FIX-12M-26-10-01/electricity-tariffs/E-1R-IOG-SMB-FIX-12M-26-10-01-F/night-unit-rates/").ShouldBeTrue();

        EnergyRate current = await sut.GetCurrentElectricityImportRateAsync();
        current.EndTimeUtc.ShouldNotBe(default);
        new[] { Day, Night }.ShouldContain(current.RateIncVat);
    }

    [TestMethod]
    public async Task OctopusReader_FourRateTariffWhoseStandardRatesEndpointIs404_StillPrices()
    {
        StubOctopus octopus = StubOctopus.FourRate();
        octopus.Respond("/standard-unit-rates/", HttpStatusCode.NotFound, """{"detail":"Not found."}""");
        OctopusReader sut = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> rates = await sut.GetElectricityImportRatesAsync(now, now.AddDays(1));

        rates.ShouldNotBeEmpty();
    }

    [TestMethod]
    public async Task OctopusReader_LegacyStandardUnitRates_AreUsedAsIsAndDayNightAreNeverAsked()
    {
        // INTELLI-FIX sliced standard-unit-rates into time-bounded records: 22:30Z-04:30Z cheap.
        StubOctopus octopus = StubOctopus.Legacy("E-1R-INTELLI-FIX-25-08-29-F", "INTELLI-FIX-25-08-29", $$"""
            {"count":3,"next":null,"previous":null,"results":[
              {"value_exc_vat":23.9251,"value_inc_vat":25.1214,"valid_from":"{{IsoOffset(DateTime.UtcNow.AddHours(5))}}","valid_to":null,"payment_method":null},
              {"value_exc_vat":3.1686,"value_inc_vat":3.327,"valid_from":"{{IsoOffset(DateTime.UtcNow.AddHours(-1))}}","valid_to":"{{IsoOffset(DateTime.UtcNow.AddHours(5))}}","payment_method":null},
              {"value_exc_vat":23.9251,"value_inc_vat":25.1214,"valid_from":"{{IsoOffset(DateTime.UtcNow.AddHours(-9))}}","valid_to":"{{IsoOffset(DateTime.UtcNow.AddHours(-1))}}","payment_method":null}
            ]}
            """);
        OctopusReader sut = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> rates = await sut.GetElectricityImportRatesAsync(now.AddHours(-12), now.AddHours(12));

        rates.Count.ShouldBe(3);
        rates.Select(r => r.RateIncVat).ShouldBe([25.1214, 3.327, 25.1214], ignoreOrder: true);
        (await sut.GetCurrentElectricityImportRateAsync()).RateIncVat.ShouldBe(3.327, 0.0001);
        octopus.Requested("day-unit-rates").ShouldBeFalse();
        octopus.Requested("night-unit-rates").ShouldBeFalse();
    }

    [TestMethod]
    public async Task OctopusReader_Export_IsNeverPricedFromDayNightRates()
    {
        // Even if the export tariff's standard-unit-rates came back empty, we must not go looking for
        // day/night endpoints it does not have; export stays exactly as it was.
        StubOctopus octopus = StubOctopus.FourRate(includeExport: true);
        octopus.Respond("OUTGOING-VAR-24-10-26/electricity-tariffs/E-1R-OUTGOING-VAR-24-10-26-F/standard-unit-rates/", HttpStatusCode.OK, EmptyRates);
        OctopusReader sut = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        (await sut.GetElectricityExportRatesAsync(now, now.AddDays(1))).ShouldBeEmpty();
        (await sut.GetElectricityImportRatesAsync(now, now.AddDays(1))).ShouldNotBeEmpty();

        octopus.Requested("OUTGOING-VAR-24-10-26/electricity-tariffs/E-1R-OUTGOING-VAR-24-10-26-F/day-unit-rates").ShouldBeFalse();
        octopus.Requested("OUTGOING-VAR-24-10-26/electricity-tariffs/E-1R-OUTGOING-VAR-24-10-26-F/night-unit-rates").ShouldBeFalse();
    }

    [TestMethod]
    public async Task OctopusReader_Export_StillReadsStandardUnitRates()
    {
        StubOctopus octopus = StubOctopus.FourRate(includeExport: true);
        octopus.Respond("OUTGOING-VAR-24-10-26/electricity-tariffs/E-1R-OUTGOING-VAR-24-10-26-F/standard-unit-rates/", HttpStatusCode.OK, $$"""
            {"count":1,"next":null,"previous":null,"results":[
              {"value_exc_vat":11.4286,"value_inc_vat":12.0,"valid_from":"{{IsoOffset(DateTime.UtcNow.AddDays(-30))}}","valid_to":null,"payment_method":null}]}
            """);
        OctopusReader sut = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> export = await sut.GetElectricityExportRatesAsync(now, now.AddDays(1));

        export.Count.ShouldBe(1);
        export[0].RateIncVat.ShouldBe(12.0, 0.0001);
    }

    // ---- The fallback ----

    [TestMethod]
    public async Task EmptyFourRateResponse_StillReachesTheHomeAssistantFallback()
    {
        StubOctopus octopus = StubOctopus.FourRate();
        octopus.Respond("/day-unit-rates/", HttpStatusCode.OK, EmptyRates);
        octopus.Respond("/night-unit-rates/", HttpStatusCode.OK, EmptyRates);
        OctopusReader reader = new(new HttpClient(octopus), Configuration(), London);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> fromHomeAssistant = [new() { StartTimeUtc = now, EndTimeUtc = now.AddMinutes(30), RateIncVat = 7.6 }];
        IHomeAssistantImportRates homeAssistant = Substitute.For<IHomeAssistantImportRates>();
        homeAssistant.GetImportRates(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(fromHomeAssistant);
        FallbackElectricityRatesReader sut = new(reader, homeAssistant, new FakeTimeProvider(), NullLogger<FallbackElectricityRatesReader>.Instance);

        List<EnergyRate> rates = await sut.GetElectricityImportRatesAsync(now, now.AddHours(1));

        rates.ShouldBe(fromHomeAssistant);
    }

    [TestMethod]
    public async Task WorkingFourRateRest_WinsOverHomeAssistant()
    {
        // The deliberate consequence of REST-first: once REST can price the tariff, HA is not asked.
        // Dispatch awareness comes from BatteryControlService's live sensor override, not from here.
        StubOctopus octopus = StubOctopus.FourRate();
        OctopusReader reader = new(new HttpClient(octopus), Configuration(), London);
        IHomeAssistantImportRates homeAssistant = Substitute.For<IHomeAssistantImportRates>();
        FallbackElectricityRatesReader sut = new(reader, homeAssistant, new FakeTimeProvider(), NullLogger<FallbackElectricityRatesReader>.Instance);

        DateTime now = DateTime.UtcNow;
        List<EnergyRate> rates = await sut.GetElectricityImportRatesAsync(now, now.AddDays(1));

        rates.ShouldNotBeEmpty();
        homeAssistant.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- Helpers ----

    private const string EmptyRates = """{"count":0,"next":null,"previous":null,"results":[]}""";

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static OctopusTariffRate RateRecord(double inc, DateTime validFrom, DateTime? validTo) => new()
    {
        ValueIncVat = (decimal)inc,
        ValueExcVat = (decimal)(inc / 1.05),
        ValidFrom = validFrom,
        ValidTo = validTo
    };

    private static List<OctopusTariffRate> Synthesise(DateTime from, DateTime to) =>
        FourRateTariff.Synthesise(
            [RateRecord(Day, Utc(2026, 10, 1, 0, 0), null)],
            [RateRecord(Night, Utc(2026, 10, 1, 0, 0), null)],
            from, to, London);

    private static List<(DateTime Start, DateTime End, double Rate)> Summarise(List<OctopusTariffRate> rates) =>
        rates.Select(r => (r.ValidFrom.ToUniversalTime(), r.ValidTo!.Value.ToUniversalTime(), (double)r.ValueIncVat)).ToList();

    private static OctopusConfiguration Configuration() => new() { AccountId = "A-TEST", Token = "test" };

    private static string IsoOffset(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>
    /// A fake of the slice of Octopus's API the reader touches. Rules match by substring of the
    /// request URL, most recently added first; anything unmatched is a 404, like the real API.
    /// </summary>
    private sealed class StubOctopus : HttpMessageHandler
    {
        private readonly List<(string Fragment, HttpStatusCode Status, string Body)> _rules = [];
        private readonly List<string> _requests = [];

        public void Respond(string urlFragment, HttpStatusCode status, string body) =>
            _rules.Insert(0, (urlFragment, status, body));

        public bool Requested(string urlFragment) => _requests.Any(r => r.Contains(urlFragment));

        public static StubOctopus FourRate(bool includeExport = false)
        {
            const string tariff = "E-1R-IOG-SMB-FIX-12M-26-10-01-F";
            StubOctopus stub = new();
            stub.Respond("/accounts/", HttpStatusCode.OK, Account(tariff, includeExport));
            stub.Respond($"/{tariff}/standard-unit-rates/", HttpStatusCode.OK, EmptyRates);
            // Single open-ended records, as published: valid from 1 October, no end.
            stub.Respond($"/{tariff}/day-unit-rates/", HttpStatusCode.OK, OpenEnded(33.92038, 35.6164));
            stub.Respond($"/{tariff}/night-unit-rates/", HttpStatusCode.OK, OpenEnded(7.238095, 7.6));
            return stub;
        }

        public static StubOctopus Legacy(string tariff, string product, string standardRates)
        {
            StubOctopus stub = new();
            stub.Respond("/accounts/", HttpStatusCode.OK, Account(tariff, includeExport: false));
            stub.Respond($"/products/{product}/electricity-tariffs/{tariff}/standard-unit-rates/", HttpStatusCode.OK, standardRates);
            return stub;
        }

        private static string OpenEnded(double exc, double inc) => $$"""
            {"count":1,"next":null,"previous":null,"results":[
              {"value_exc_vat":{{exc}},"value_inc_vat":{{inc}},"valid_from":"2026-10-01T00:00:00Z","valid_to":null,"payment_method":null}]}
            """;

        private static string Account(string importTariff, bool includeExport)
        {
            string export = includeExport
                ? $$"""
                  ,{"mpan":"2","is_export":true,"agreements":[{"tariff_code":"E-1R-OUTGOING-VAR-24-10-26-F","valid_from":"2025-01-01T00:00:00Z","valid_to":null}]}
                  """
                : string.Empty;

            return $$"""
                {"number":"A-TEST","properties":[{"id":1,"moved_in_at":"2020-01-01T00:00:00Z","moved_out_at":null,
                  "electricity_meter_points":[
                    {"mpan":"1","is_export":false,"agreements":[{"tariff_code":"{{importTariff}}","valid_from":"2026-10-09T00:00:00Z","valid_to":null}]}
                    {{export}}
                  ]}]}
                """;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            _requests.Add(url);

            foreach ((string fragment, HttpStatusCode status, string body) in _rules)
            {
                if (url.Contains(fragment))
                {
                    return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"detail":"Not found."}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
