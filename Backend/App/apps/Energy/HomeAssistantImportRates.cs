using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HomeAssistant.apps.Energy;

/// <summary>
/// Import rates as Home Assistant's Octopus integration has already worked them out.
/// </summary>
internal interface IHomeAssistantImportRates
{
    /// <summary>Rates overlapping [fromUtc, toUtc), in p/kWh, oldest first. Empty when HA has none.</summary>
    List<EnergyRate> GetImportRates(DateTime fromUtc, DateTime toUtc);

    /// <summary>The rate covering <paramref name="nowUtc"/> in p/kWh, or null when HA has none.</summary>
    EnergyRate? GetCurrentImportRate(DateTime nowUtc);
}

/// <summary>
/// Reads the import tariff's rate lists from the BottlecapDave Octopus Energy integration's
/// <c>event.octopus_energy_electricity_{serial}_{mpan}_{previous|current|next}_day_rates</c>
/// entities. This exists because Octopus's public REST endpoint publishes no unit rates for
/// the Intelligent Octopus Go 12M Fixed tariffs (<c>IOG-SMB-FIX</c>), so <see cref="Octopus.OctopusReader"/>
/// comes back empty for them, while the integration reads the account and prices them fine.
///
/// The events cover yesterday, today and tomorrow (48 half-hourly slots each) and carry their
/// rates in GBP/kWh inc VAT; the app works in p/kWh, hence the x100. The export meter has the
/// same events with an <c>_export_</c> infix, which is deliberately excluded - export still
/// prices correctly over REST.
/// </summary>
internal class HomeAssistantImportRates : IHomeAssistantImportRates
{
    private const string EntityPrefix = "event.octopus_energy_electricity_";
    private static readonly string[] DaySuffixes = ["_previous_day_rates", "_current_day_rates", "_next_day_rates"];

    // Octopus quotes domestic electricity at 5% VAT. The events only carry the inc-VAT figure.
    private const double VatMultiplier = 1.05;

    private readonly IHaContext _ha;

    public HomeAssistantImportRates(IHaContext ha)
    {
        _ha = ha;
    }

    public List<EnergyRate> GetImportRates(DateTime fromUtc, DateTime toUtc)
    {
        return ReadAllRates()
            .Where(r => r.StartTimeUtc < toUtc && r.EndTimeUtc > fromUtc)
            .ToList();
    }

    public EnergyRate? GetCurrentImportRate(DateTime nowUtc)
    {
        return ReadAllRates().FirstOrDefault(r => r.StartTimeUtc <= nowUtc && r.EndTimeUtc > nowUtc);
    }

    private List<EnergyRate> ReadAllRates()
    {
        string? meterPrefix = FindImportMeterPrefix(_ha.GetAllEntities().Select(e => e.EntityId));
        if (meterPrefix == null)
        {
            return [];
        }

        Dictionary<DateTime, EnergyRate> bySlotStart = [];
        foreach (string suffix in DaySuffixes)
        {
            JsonElement? attributes = _ha.GetState(meterPrefix + suffix)?.AttributesJson;
            if (!attributes.HasValue)
            {
                continue;
            }

            foreach (EnergyRate rate in ParseRates(attributes.Value))
            {
                bySlotStart[rate.StartTimeUtc] = rate;
            }
        }

        return [.. bySlotStart.Values.OrderBy(r => r.StartTimeUtc)];
    }

    /// <summary>
    /// Finds the entity-ID prefix (everything before the day suffix) of the import meter's rate
    /// events. If the account somehow exposes more than one import meter the first by ID wins,
    /// consistently, rather than mixing two meters' slots together.
    /// </summary>
    internal static string? FindImportMeterPrefix(IEnumerable<string> entityIds)
    {
        return entityIds
            .Where(id => id.StartsWith(EntityPrefix, StringComparison.OrdinalIgnoreCase)
                         && id.EndsWith("_current_day_rates", StringComparison.OrdinalIgnoreCase)
                         && !id.Contains("_export_", StringComparison.OrdinalIgnoreCase))
            .Select(id => id[..^"_current_day_rates".Length])
            .OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Parses an event's <c>rates</c> attribute (GBP/kWh) into p/kWh <see cref="EnergyRate"/>s.
    /// Slots that are malformed are skipped rather than failing the whole list.
    /// </summary>
    internal static List<EnergyRate> ParseRates(JsonElement attributes)
    {
        List<EnergyRate> rates = [];
        if (attributes.ValueKind != JsonValueKind.Object
            || !attributes.TryGetProperty("rates", out JsonElement ratesElement)
            || ratesElement.ValueKind != JsonValueKind.Array)
        {
            return rates;
        }

        foreach (JsonElement slot in ratesElement.EnumerateArray())
        {
            if (slot.ValueKind != JsonValueKind.Object
                || !TryReadTime(slot, "start", out DateTime start)
                || !TryReadTime(slot, "end", out DateTime end)
                || !slot.TryGetProperty("value_inc_vat", out JsonElement value)
                || value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            double incVatPence = value.GetDouble() * 100;
            rates.Add(new EnergyRate
            {
                StartTimeUtc = start,
                EndTimeUtc = end,
                RateIncVat = incVatPence,
                RateExcVat = incVatPence / VatMultiplier
            });
        }

        return rates;
    }

    private static bool TryReadTime(JsonElement slot, string property, out DateTime utc)
    {
        utc = default;
        if (slot.TryGetProperty(property, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
        {
            utc = parsed.UtcDateTime;
            return true;
        }

        return false;
    }
}
