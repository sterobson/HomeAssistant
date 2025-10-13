using System.Threading.Tasks;

namespace HomeAssistant.apps.Energy;

public interface IHomeBatteryManager
{
    public Task<HomeBatteryState> GetHomeBatteryStateAsync();
}

public class HomeBatteryState
{
    public bool IsCharging { get; set; }
    public bool IsDischarging { get; set; }
    public double ChargeLevel { get; set; }
    public double ChargePower { get; set; }
    public double DischargePower { get; set; }
    public double MaxChargePower { get; set; }
    public double MaxDischargePower { get; set; }
    public double BatteryCapacity { get; set; }
}