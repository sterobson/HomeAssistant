using HomeAssistant.apps.Energy.Octopus;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The app asks Octopus for rates from a day ago to three days ahead and builds a pricing slot list for each
/// of today, tomorrow and the day after. A four-rate tariff publishes just two open-ended records, so every
/// day's cheap night window has to be synthesised, not only today's. These use the real records for the
/// account's tariff (day 35.6164p and night 7.6p, both valid from 2026-09-30T23:00Z, open-ended).
/// </summary>
[TestClass]
public sealed class FourRateMultiDayTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static List<OctopusTariffRate> Open(decimal pence) =>
    [
        new OctopusTariffRate
        {
            ValueIncVat = pence,
            ValueExcVat = pence,
            ValidFrom = new DateTime(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc),
            ValidTo = null
        }
    ];

    [TestMethod]
    public void EveryDayOfAFourDayWindowGetsItsOwnNightRate()
    {
        // The window the reader requests at about 19:30 UTC on 17 Oct: now - 1 day to now + 3 days.
        DateTime from = new(2026, 10, 16, 19, 30, 0, DateTimeKind.Utc);
        DateTime to = new(2026, 10, 20, 19, 30, 0, DateTimeKind.Utc);

        List<OctopusTariffRate> result = FourRateTariff.Synthesise(Open(35.6164m), Open(7.6m), from, to, London);

        // For each local day inside the window there must be a rate that STARTS at 05:30 (night to day)
        // and one that starts at 23:30 (day to night). A flat day means the night window was never applied.
        foreach (DateTime localDay in new[] { new DateTime(2026, 10, 17), new DateTime(2026, 10, 18), new DateTime(2026, 10, 19) })
        {
            DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDay, London);
            DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localDay.AddDays(1), London);

            List<(int Minute, decimal Rate)> startsToday = [.. result
                .Where(r => r.ValidFrom >= dayStartUtc && r.ValidFrom < dayEndUtc)
                .Select(r => ((int)(TimeZoneInfo.ConvertTimeFromUtc(r.ValidFrom, London) - localDay).TotalMinutes, r.ValueIncVat))];

            startsToday.ShouldContain((5 * 60 + 30, 35.6164m), $"{localDay:yyyy-MM-dd}: day rate should start at 05:30");
            startsToday.ShouldContain((23 * 60 + 30, 7.6m), $"{localDay:yyyy-MM-dd}: night rate should start at 23:30");
        }
    }
}
