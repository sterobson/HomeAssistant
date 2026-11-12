using HomeAssistant.apps.Energy;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

/// <summary>
/// A rate sensor's history over a window, in £/kWh, in which every stretch the sensor was not
/// reporting a number has been filled in from the rates Octopus publishes.
/// </summary>
internal sealed class EnergyRateHistory
{
    /// <summary>Numeric rate changes in time order; the first starts at or after the window start.</summary>
    public IReadOnlyList<NumericHistoryEntry> Entries { get; init; } = [];

    /// <summary>
    /// Stretches of the window for which no rate could be found anywhere. <see cref="Entries"/> says nothing
    /// about them, so costing across one would use whichever rate came before it.
    /// </summary>
    public IReadOnlyList<(DateTime FromUtc, DateTime ToUtc)> Unpriced { get; init; } = [];

    public bool IsPricedThroughout(DateTime fromUtc, DateTime toUtc) =>
        !Unpriced.Any(u => u.FromUtc < toUtc && u.ToUtc > fromUtc);
}

internal interface IEnergyRateHistoryReader
{
    /// <summary>
    /// The rate sensor's history for [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) with any gap filled from
    /// Octopus (import or export, as <paramref name="isExport"/> says).
    /// </summary>
    Task<EnergyRateHistory> GetAsync(string sensorId, bool isExport, DateTime fromUtc, DateTime toUtc);
}

/// <summary>
/// Reads a rate sensor's history without mistaking "the sensor was down" for "electricity was free".
///
/// <see cref="HistoryService.GetEntityNumericHistory"/> turns <c>unavailable</c> into 0, so when Home Assistant's
/// Octopus integration failed to start after an unclean restart (HOME-ASSISTANT.md gotcha 36) every hour
/// afterwards was costed at 0p, and the battery price slots for the day gained a 0p slot, with no error anywhere.
/// This reads the text history instead, keeps the numeric stretches as they were and replaces each other stretch
/// with Octopus's published rates (the same REST source the battery planner uses; pence there, pounds here). What
/// Octopus cannot supply is reported in <see cref="EnergyRateHistory.Unpriced"/> so the caller can say so instead of
/// storing a made-up zero.
/// </summary>
internal sealed class EnergyRateHistoryReader : IEnergyRateHistoryReader
{
    private readonly HistoryService _historyService;
    private readonly IElectricityRatesReader _ratesReader;
    private readonly ILogger<EnergyRateHistoryReader> _logger;

    public EnergyRateHistoryReader(
        HistoryService historyService,
        IElectricityRatesReader ratesReader,
        ILogger<EnergyRateHistoryReader> logger)
    {
        _historyService = historyService;
        _ratesReader = ratesReader;
        _logger = logger;
    }

    public async Task<EnergyRateHistory> GetAsync(string sensorId, bool isExport, DateTime fromUtc, DateTime toUtc)
    {
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();

        IReadOnlyList<HistoryTextEntry> states = await _historyService.GetEntityTextHistory(sensorId, fromUtc, toUtc);

        List<NumericHistoryEntry> entries = [];
        List<(DateTime FromUtc, DateTime ToUtc)> gaps = [];

        List<HistoryTextEntry> ordered = [.. states.OrderBy(s => s.LastChanged)];
        DateTime coveredUntil = fromUtc;
        for (int i = 0; i < ordered.Count; i++)
        {
            DateTime start = ordered[i].LastChanged.ToUniversalTime();
            if (start < fromUtc) start = fromUtc;

            DateTime end = i + 1 < ordered.Count ? ordered[i + 1].LastChanged.ToUniversalTime() : toUtc;
            if (end > toUtc) end = toUtc;
            if (end <= start) continue;

            // The sensor had no recorded state before this one: nothing was known for that stretch.
            if (start > coveredUntil) AddGap(gaps, coveredUntil, start);

            if (double.TryParse(ordered[i].State, NumberStyles.Float, CultureInfo.InvariantCulture, out double poundsPerKwh))
            {
                entries.Add(new NumericHistoryEntry { LastChanged = start, State = poundsPerKwh });
            }
            else
            {
                // "unavailable", "unknown", or nothing at all.
                AddGap(gaps, start, end);
            }

            coveredUntil = end;
        }

        if (coveredUntil < toUtc) AddGap(gaps, coveredUntil, toUtc);

        List<(DateTime FromUtc, DateTime ToUtc)> unpriced = [];
        foreach ((DateTime gapFrom, DateTime gapTo) in gaps)
        {
            await FillGapAsync(sensorId, isExport, gapFrom, gapTo, entries, unpriced);
        }

        return new EnergyRateHistory
        {
            Entries = [.. entries.OrderBy(e => e.LastChanged)],
            Unpriced = unpriced
        };
    }

    private async Task FillGapAsync(
        string sensorId,
        bool isExport,
        DateTime gapFrom,
        DateTime gapTo,
        List<NumericHistoryEntry> entries,
        List<(DateTime FromUtc, DateTime ToUtc)> unpriced)
    {
        List<EnergyRate> rates;
        try
        {
            rates = isExport
                ? await _ratesReader.GetElectricityExportRatesAsync(gapFrom, gapTo)
                : await _ratesReader.GetElectricityImportRatesAsync(gapFrom, gapTo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Octopus's {Direction} rates for {From:u} to {To:u}",
                isExport ? "export" : "import", gapFrom, gapTo);
            unpriced.Add((gapFrom, gapTo));
            return;
        }

        // Octopus reports an empty rate (end time unset) when it knows nothing; ignore those.
        DateTime cursor = gapFrom;
        foreach (EnergyRate rate in rates.Where(r => r.EndTimeUtc != default).OrderBy(r => r.StartTimeUtc))
        {
            if (rate.EndTimeUtc <= cursor) continue;
            if (rate.StartTimeUtc >= gapTo) break;

            DateTime start = rate.StartTimeUtc > cursor ? rate.StartTimeUtc : cursor;
            if (start > cursor) unpriced.Add((cursor, start));

            // Octopus quotes pence per kWh, the sensors pounds.
            entries.Add(new NumericHistoryEntry { LastChanged = start, State = rate.RateIncVat / 100.0 });
            cursor = rate.EndTimeUtc < gapTo ? rate.EndTimeUtc : gapTo;
        }

        if (cursor < gapTo) unpriced.Add((cursor, gapTo));

        if (cursor == gapFrom)
        {
            _logger.LogWarning(
                "{Sensor} reported no rate from {From:u} to {To:u} and Octopus has none for it either",
                sensorId, gapFrom, gapTo);
        }
        else
        {
            _logger.LogInformation(
                "{Sensor} reported no rate from {From:u} to {To:u}; using the rates Octopus publishes",
                sensorId, gapFrom, gapTo);
        }
    }

    private static void AddGap(List<(DateTime FromUtc, DateTime ToUtc)> gaps, DateTime from, DateTime to)
    {
        if (to <= from) return;

        if (gaps.Count > 0 && gaps[^1].ToUtc == from)
        {
            gaps[^1] = (gaps[^1].FromUtc, to);
        }
        else
        {
            gaps.Add((from, to));
        }
    }
}
