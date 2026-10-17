using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.apps.Energy.Octopus;

/// <summary>
/// Turns the two open-ended rates of a four-rate EV tariff (the Intelligent Octopus Go 12M Fixed
/// products, <c>IOG-SMB-*</c>, from October 2026) into the time-bounded records the rest of the app
/// expects.
///
/// The old INTELLI-FIX tariff published the cheap window by slicing <c>standard-unit-rates</c>
/// into time-bounded records. The four-rate products instead publish one open-ended
/// <c>day-unit-rates</c> record and one open-ended <c>night-unit-rates</c> record and say nothing
/// about when each applies, so the window has to be known here.
/// </summary>
internal static class FourRateTariff
{
    // The night rate applies 23:30-05:30 every night, local time. This comes from the product
    // description ("23:30 - 05:30 every night"), NOT from the API, which only publishes the two
    // values; Home Assistant's Octopus integration hard-codes the same window. If Octopus ever
    // changes the product the API will not tell us - the description is the source to re-check.
    // The window crosses midnight, so it is expressed as a start and an end rather than a range.
    internal static readonly TimeOnly NightWindowStart = new(23, 30);
    internal static readonly TimeOnly NightWindowEnd = new(5, 30);

    /// <summary>
    /// Time-bounded records covering [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), choosing the night
    /// record inside the night window and the day record outside it, with the window evaluated in
    /// <paramref name="zone"/> so it follows BST/GMT. Records are also split wherever a day or night rate itself
    /// changes, so a mid-window price change is honoured. A stretch with no rate on record is omitted.
    /// </summary>
    internal static List<OctopusTariffRate> Synthesise(
        IReadOnlyList<OctopusTariffRate> dayRates,
        IReadOnlyList<OctopusTariffRate> nightRates,
        DateTime fromUtc,
        DateTime toUtc,
        TimeZoneInfo zone)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        if (toUtc <= fromUtc)
        {
            return [];
        }

        SortedSet<DateTime> instants = [fromUtc, toUtc];

        foreach (OctopusTariffRate rate in dayRates.Concat(nightRates))
        {
            AddIfInside(instants, rate.ValidFrom.ToUniversalTime(), fromUtc, toUtc);
            if (rate.ValidTo != null)
            {
                AddIfInside(instants, rate.ValidTo.Value.ToUniversalTime(), fromUtc, toUtc);
            }
        }

        // The window edges. Start a day early and end a day late so a window already under way at
        // fromUtc, or one that starts just before toUtc, is not missed.
        DateTime firstLocalDate = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone).Date.AddDays(-1);
        DateTime lastLocalDate = TimeZoneInfo.ConvertTimeFromUtc(toUtc, zone).Date.AddDays(1);
        for (DateTime date = firstLocalDate; date <= lastLocalDate; date = date.AddDays(1))
        {
            AddIfInside(instants, LocalToUtc(date + NightWindowEnd.ToTimeSpan(), zone), fromUtc, toUtc);
            AddIfInside(instants, LocalToUtc(date + NightWindowStart.ToTimeSpan(), zone), fromUtc, toUtc);
        }

        List<DateTime> boundaries = [.. instants];
        List<OctopusTariffRate> result = [];

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            DateTime start = boundaries[i];
            DateTime end = boundaries[i + 1];

            bool isNight = IsNight(TimeZoneInfo.ConvertTimeFromUtc(start, zone));
            IReadOnlyList<OctopusTariffRate> source = isNight ? nightRates : dayRates;
            OctopusTariffRate? rate = source.FirstOrDefault(r =>
                r.ValidFrom.ToUniversalTime() <= start && (r.ValidTo == null || r.ValidTo.Value.ToUniversalTime() > start));
            if (rate == null)
            {
                continue;
            }

            OctopusTariffRate? previous = result.Count > 0 ? result[^1] : null;
            if (previous != null
                && previous.ValidTo == start
                && previous.ValueIncVat == rate.ValueIncVat
                && previous.ValueExcVat == rate.ValueExcVat)
            {
                previous.ValidTo = end;
                continue;
            }

            result.Add(new OctopusTariffRate
            {
                ValueIncVat = rate.ValueIncVat,
                ValueExcVat = rate.ValueExcVat,
                PaymentMethod = rate.PaymentMethod,
                ValidFrom = start,
                ValidTo = end
            });
        }

        return result;
    }

    /// <summary>True when a local wall-clock time falls inside the night window.</summary>
    internal static bool IsNight(DateTime localTime)
    {
        TimeOnly time = TimeOnly.FromDateTime(localTime);
        return time >= NightWindowStart || time < NightWindowEnd;
    }

    private static void AddIfInside(SortedSet<DateTime> instants, DateTime instantUtc, DateTime fromUtc, DateTime toUtc)
    {
        if (instantUtc > fromUtc && instantUtc < toUtc)
        {
            instants.Add(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc));
        }
    }

    private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        // A wall-clock time skipped by the spring-forward does not exist; nudge it past the gap.
        // (Neither window edge falls in a UK transition hour, but the zone is a parameter.)
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }
}
