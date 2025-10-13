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
    private OctopusTariffRates _rates = new();
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

        return _rates.Results
            .Where(r => r.ValidFrom <= DateTime.UtcNow && r.ValidTo > DateTime.UtcNow)
            .FirstOrDefault()?
            .ToEnergyRate() ?? new();
    }

    public async Task<List<EnergyRate>> GetElectricityImportRatesAsync(DateTime from, DateTime to)
    {
        await RefreshRatesIfNeeded();

        return _rates.Results
            .Where(r => r.ValidFrom <= DateTime.UtcNow && r.ValidTo > DateTime.UtcNow)
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
            ElectricityMeterPoint electricityMeterPoint = currentProperty.ElectricityMeterPoints.FirstOrDefault(m => !m.IsExport) ?? new();
            ElectricityMeter currentMeter = electricityMeterPoint.Meters.LastOrDefault() ?? new();
            Agreement currentAgreement = electricityMeterPoint.Agreements.LastOrDefault(a => a.ValidFrom < DateTime.UtcNow && (a.ValidTo == null || a.ValidTo > DateTime.UtcNow)) ?? new();

            string tariffCode = currentAgreement.TariffCode;
            if (string.IsNullOrEmpty(tariffCode))
            {
                return;
            }

            string[] tariffCodeParts = tariffCode.Split('-');
            string productCode = string.Join('-', tariffCodeParts[2..^1]);

            OctopusTariffRates ratesResult = await GetTariffRatesAsync(productCode, tariffCode) ?? new();

            _rates = ratesResult;
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

        string requestUri = $"https://api.octopus.energy/v1/products/{productCode}/electricity-tariffs/{tariffCode}/standard-unit-rates/";
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