using System.Net.Http;
using System.Threading.Tasks;

namespace HomeAssistant.apps.Energy.Solax;

// General plan for Solax - always charge at night when rates are low
// During the day use solar to power the house and charge the battery
public class SolaxBatteryManager : IHomeBatteryManager
{
    private readonly HttpClient _httpClient;
    private readonly SolaxConfiguration _configuration;

    public SolaxBatteryManager(HttpClient httpClient, SolaxConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public Task<HomeBatteryState> GetHomeBatteryStateAsync()
    {
        throw new NotImplementedException();
    }
}
