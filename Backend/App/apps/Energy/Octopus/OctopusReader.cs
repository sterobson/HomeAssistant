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

    public OctopusReader(HttpClient httpClient, OctopusConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
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
                OctopusTariffRates importRatesResult = await GetTariffRatesAsync(importProductCode, importTariffCode) ?? new();
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
                OctopusTariffRates exportRatesResult = await GetTariffRatesAsync(exportProductCode, exportTariffCode) ?? new();
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

    private async Task<OctopusTariffRates?> GetTariffRatesAsync(string productCode, string tariffCode)
    {
        if (string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(tariffCode))
        {
            return null;
        }

        string periodFrom = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        string periodTo = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        string requestUri = $"https://api.octopus.energy/v1/products/{productCode}/electricity-tariffs/{tariffCode}/standard-unit-rates/?period_from={periodFrom}&period_to={periodTo}&page_size=500";
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
