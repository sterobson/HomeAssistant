using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.apps.Energy;

/// <summary>
/// Octopus's REST reader first, Home Assistant's Octopus integration when REST has nothing.
///
/// REST is tried first because it is what every consumer has always used and it still works for
/// export. But it silently returns an empty list for tariffs the public endpoint does not
/// publish (the Intelligent Octopus Go 12M Fixed tariffs from October 2026), and an empty list
/// used to fall through to a default <see cref="EnergyRate"/> of 0 - a price of free. For import
/// we therefore fall back to the rates HA has already priced, and if that is empty too we say so
/// loudly, once, instead of letting 0p flow into battery planning and stored pricing.
///
/// Export is passed straight through: HA fallback is import-only by design.
///
/// REST-first is deliberate, including now that OctopusReader prices the four-rate IOG-SMB-*
/// tariffs. REST there is plain day/night (23:30-05:30) with no knowledge of smart-charge
/// dispatches, whereas HA's events may include them, so HA can look like the better source.
/// It is not preferred because dispatch awareness is not what the rates feed is for:
/// BatteryControlService already takes the live HA import-rate sensor as an override
/// (ReactToRateChangeAsync) and overlays it on the cached rates whenever the sensor changes,
/// which is how a dispatch window - or the ordinary 23:30 switch - reaches battery control.
/// That is exactly the behaviour the app had on INTELLI-FIX, whose REST rates also knew nothing
/// of dispatches. The cost is that a *planned* dispatch is invisible until it starts; the HA
/// events do not clearly fix that either (open integration bugs on IOG dispatch sensors:
/// #1884, #1892, #1898, #1901). If forward visibility of dispatches is ever wanted, merge
/// HA's cheap slots into REST's here rather than swapping the order. See HOME-ASSISTANT.md.
/// </summary>
internal class FallbackElectricityRatesReader : IElectricityRatesReader
{
    private readonly IElectricityRatesReader _primary;
    private readonly IHomeAssistantImportRates _homeAssistant;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FallbackElectricityRatesReader> _logger;

    // Warn on the transition into "no import rates", not on every call - the callers poll every
    // few minutes. Cleared as soon as rates come back so a later regression is reported again.
    private bool _warnedNoImportRates;
    private bool _loggedFallbackInUse;

    public FallbackElectricityRatesReader(
        IElectricityRatesReader primary,
        IHomeAssistantImportRates homeAssistant,
        TimeProvider timeProvider,
        ILogger<FallbackElectricityRatesReader> logger)
    {
        _primary = primary;
        _homeAssistant = homeAssistant;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EnergyRate> GetCurrentElectricityImportRateAsync()
    {
        EnergyRate rate = await _primary.GetCurrentElectricityImportRateAsync();
        if (!IsEmpty(rate))
        {
            RatesAvailable(viaFallback: false);
            return rate;
        }

        EnergyRate? fromHomeAssistant = _homeAssistant.GetCurrentImportRate(_timeProvider.GetUtcNow().UtcDateTime);
        if (fromHomeAssistant != null)
        {
            RatesAvailable(viaFallback: true);
            return fromHomeAssistant;
        }

        WarnNoImportRates("current rate");
        return rate;
    }

    public async Task<List<EnergyRate>> GetElectricityImportRatesAsync(DateTime from, DateTime to)
    {
        List<EnergyRate> rates = await _primary.GetElectricityImportRatesAsync(from, to);
        if (rates.Count > 0)
        {
            RatesAvailable(viaFallback: false);
            return rates;
        }

        List<EnergyRate> fromHomeAssistant = _homeAssistant.GetImportRates(from, to);
        if (fromHomeAssistant.Count > 0)
        {
            RatesAvailable(viaFallback: true);
            return fromHomeAssistant;
        }

        WarnNoImportRates($"{from:u} to {to:u}");
        return rates;
    }

    public Task<EnergyRate> GetCurrentElectricityExportRateAsync() => _primary.GetCurrentElectricityExportRateAsync();

    public Task<List<EnergyRate>> GetElectricityExportRatesAsync(DateTime from, DateTime to) =>
        _primary.GetElectricityExportRatesAsync(from, to);

    // OctopusReader returns a default-constructed rate when nothing matches.
    private static bool IsEmpty(EnergyRate rate) => rate.EndTimeUtc == default;

    private void RatesAvailable(bool viaFallback)
    {
        _warnedNoImportRates = false;

        if (viaFallback && !_loggedFallbackInUse)
        {
            _loggedFallbackInUse = true;
            _logger.LogInformation(
                "Octopus REST returned no import rates; using the rates Home Assistant's Octopus integration publishes instead");
        }
        else if (!viaFallback)
        {
            _loggedFallbackInUse = false;
        }
    }

    private void WarnNoImportRates(string what)
    {
        if (_warnedNoImportRates)
        {
            return;
        }

        _warnedNoImportRates = true;
        _logger.LogWarning(
            "No electricity import rates available for {Request}: Octopus REST returned none and Home Assistant has no " +
            "octopus_energy import rate events. Import price will be treated as 0p/kWh, so battery planning and stored " +
            "pricing are unreliable until this is fixed",
            what);
    }
}
