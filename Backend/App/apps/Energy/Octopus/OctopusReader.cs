using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.Energy.Octopus;

public class OctopusReader : IElectricityRatesReader
{
    private readonly HttpClient _httpClient;
    private readonly OctopusConfiguration _configuration;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private OctopusTariffRates _importRates = new();
    private OctopusTariffRates _exportRates = new();
    private DateTime _lastRefresh;
    private const int _refreshIntervalMinutes = 30;

    // The zone the four-rate night window is evaluated in. Local, like PricingSlot and the rest of
    // the battery pipeline, so the synthesised boundaries line up with how they are bucketed.
    private readonly TimeZoneInfo _timeZone;

    public OctopusReader(HttpClient httpClient, OctopusConfiguration configuration)
        : this(httpClient, configuration, TimeZoneInfo.Local)
    {
    }

    // Internal so DI keeps resolving the public constructor; tests pin the zone to cover BST and GMT.
    internal OctopusReader(HttpClient httpClient, OctopusConfiguration configuration, TimeZoneInfo timeZone)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _timeZone = timeZone;
    }

    public async Task<EnergyRate> GetCurrentElectricityImportRateAsync()
    {
        await RefreshRatesIfNeeded();

        return _importRates.Results
            .Where(r => r.ValidFrom <= DateTime.UtcNow && (r.ValidTo == null || r.ValidTo > DateTime.UtcNow))
            .FirstOrDefault()?
            .ToEnergyRate() ?? new();
    }

    public async Task<List<EnergyRate>> GetElectricityImportRatesAsync(DateTime from, DateTime to)
    {
        await RefreshRatesIfNeeded();

        return _importRates.Results
            .Where(r => r.ValidFrom < to && (r.ValidTo == null || r.ValidTo > from))
            .Select(r => r.ToEnergyRate()).ToList();
    }

    public async Task<EnergyRate> GetCurrentElectricityExportRateAsync()
    {
        await RefreshRatesIfNeeded();

        return _exportRates.Results
            .Where(r => r.ValidFrom <= DateTime.UtcNow && (r.ValidTo == null || r.ValidTo > DateTime.UtcNow))
            .FirstOrDefault()?
            .ToEnergyRate() ?? new();
    }

    public async Task<List<EnergyRate>> GetElectricityExportRatesAsync(DateTime from, DateTime to)
    {
        await RefreshRatesIfNeeded();

        return _exportRates.Results
            .Where(r => r.ValidFrom < to && (r.ValidTo == null || r.ValidTo > from))
            .Select(r => r.ToEnergyRate()).ToList();
    }


    private async Task RefreshRatesIfNeeded()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_lastRefresh.AddMinutes(_refreshIntervalMinutes) > DateTime.UtcNow)
            {
                return;
            }

            AccountInfo? result = await GetAccountInfoAsync();
            if (result == null)
            {
                return;
            }

            Property currentProperty = result.Properties.Where(p => p.MovedInAt < DateTime.UtcNow && p.MovedOutAt == null).FirstOrDefault() ?? new();

            // Import meter point
            ElectricityMeterPoint importMeterPoint = currentProperty.ElectricityMeterPoints.FirstOrDefault(m => !m.IsExport) ?? new();
            Agreement importAgreement = importMeterPoint.Agreements.LastOrDefault(a => a.ValidFrom < DateTime.UtcNow && (a.ValidTo == null || a.ValidTo > DateTime.UtcNow)) ?? new();

            string importTariffCode = importAgreement.TariffCode;
            if (!string.IsNullOrEmpty(importTariffCode))
            {
                string[] importTariffCodeParts = importTariffCode.Split('-');
                string importProductCode = string.Join('-', importTariffCodeParts[2..^1]);
                OctopusTariffRates importRatesResult = await GetTariffRatesAsync(importProductCode, importTariffCode, allowFourRate: true) ?? new();
                _importRates = importRatesResult;
            }

            // Export meter point
            ElectricityMeterPoint exportMeterPoint = currentProperty.ElectricityMeterPoints.FirstOrDefault(m => m.IsExport) ?? new();
            Agreement exportAgreement = exportMeterPoint.Agreements.LastOrDefault(a => a.ValidFrom < DateTime.UtcNow && (a.ValidTo == null || a.ValidTo > DateTime.UtcNow)) ?? new();

            string exportTariffCode = exportAgreement.TariffCode;
            if (!string.IsNullOrEmpty(exportTariffCode))
            {
                string[] exportTariffCodeParts = exportTariffCode.Split('-');
                string exportProductCode = string.Join('-', exportTariffCodeParts[2..^1]);
                OctopusTariffRates exportRatesResult = await GetTariffRatesAsync(exportProductCode, exportTariffCode, allowFourRate: false) ?? new();
                _exportRates = exportRatesResult;
            }

            _lastRefresh = DateTime.UtcNow;
        }
        finally
        {
            _semaphore.Release();
        }

    }

    private async Task<AccountInfo?> GetAccountInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration?.AccountId) || string.IsNullOrWhiteSpace(_configuration?.Token))
        {
            return null;
        }

        string requestUri = $"https://api.octopus.energy/v1/accounts/{_configuration.AccountId}/";
        HttpRequestMessage request = new(HttpMethod.Get, requestUri);

        // Basic Auth: username is _token, password is empty
        string authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_configuration?.Token}:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync();
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<AccountInfo>(json, options);
    }

    /// <summary>
    /// Rates for a tariff over the window the cache covers (yesterday to three days ahead).
    ///
    /// <c>standard-unit-rates</c> is tried first: it is the only source for ordinary tariffs, including
    /// export, and for the old INTELLI-FIX tariff, which encoded its cheap window as time-bounded
    /// records. The four-rate EV tariffs (IOG-SMB-*) have no such link, so when it yields nothing
    /// and <paramref name="allowFourRate"/> is set we synthesise records from day-unit-rates and
    /// night-unit-rates instead - see <see cref="FourRateTariff"/>.
    ///
    /// <paramref name="allowFourRate"/> is import-only: export must behave exactly as before, so a
    /// transient empty answer for OUTGOING-VAR is not turned into a request for endpoints it
    /// does not have.
    /// </summary>
    private async Task<OctopusTariffRates?> GetTariffRatesAsync(string productCode, string tariffCode, bool allowFourRate)
    {
        if (string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(tariffCode))
        {
            return null;
        }

        DateTime from = DateTime.UtcNow.AddDays(-1);
        DateTime to = DateTime.UtcNow.AddDays(3);

        OctopusTariffRates? standard = await GetRatesAsync(productCode, tariffCode, "standard-unit-rates", from, to);
        if (standard?.Results.Count > 0 || !allowFourRate)
        {
            return standard;
        }

        OctopusTariffRates? day = await GetRatesAsync(productCode, tariffCode, "day-unit-rates", from, to);
        OctopusTariffRates? night = await GetRatesAsync(productCode, tariffCode, "night-unit-rates", from, to);
        if (day == null || night == null || day.Results.Count == 0 || night.Results.Count == 0)
        {
            // Not a four-rate tariff either (or Octopus is down): report what we had, which is nothing.
            return standard;
        }

        return new OctopusTariffRates
        {
            Results = FourRateTariff.Synthesise(day.Results, night.Results, from, to, _timeZone)
        };
    }

    private async Task<OctopusTariffRates?> GetRatesAsync(string productCode, string tariffCode, string rateType, DateTime from, DateTime to)
    {
        string periodFrom = from.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        string periodTo = to.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        string requestUri = $"https://api.octopus.energy/v1/products/{productCode}/electricity-tariffs/{tariffCode}/{rateType}/?period_from={periodFrom}&period_to={periodTo}&page_size=500";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<OctopusTariffRates>(json, options);
    }
}
