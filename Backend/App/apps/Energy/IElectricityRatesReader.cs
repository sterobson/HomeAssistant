using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.apps.Energy;

public interface IElectricityRatesReader
{
    public Task<EnergyRate> GetCurrentElectricityImportRateAsync();

    public Task<List<EnergyRate>> GetElectricityImportRatesAsync(DateTime from, DateTime to);
}
