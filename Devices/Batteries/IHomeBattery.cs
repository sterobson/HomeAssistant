using HomeAssistant.Devices.Meters;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Batteries;

public enum BatteryState
{
    NormalTOU,
    ForceCharging,
    ForceDischarging,
    Stopped,
    Unknown
}

public interface IHomeBattery
{
    double? CurrentChargePercent { get; }
    BatteryState GetHomeBatteryState();
    void OnBatteryChargePercentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action);
    void SetHomeBatteryState(BatteryState desiredHomeBatteryState);
    void SetMaxChargeCurrentHeadroom(int headroom);
}