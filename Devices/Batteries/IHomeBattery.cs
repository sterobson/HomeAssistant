using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
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
    double BatteryCapacitykWh { get; }
    BatteryState GetHomeBatteryState();
    void OnBatteryChargePercentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action);
    void OnBatteryUseModeChanged(Func<Task> action);
    void SetHomeBatteryState(BatteryState desiredHomeBatteryState);
    void SetMaxChargeCurrentHeadroom(int headroom);
    Task<IReadOnlyList<NumericHistoryEntry>> GetTotalBatteryPowerChargeHistoryEntriesAsync(DateTime from, DateTime to);
    Task<IReadOnlyList<HistoryEntry<BatteryState>>> GetBatteryStateHistoryEntriesAsync(DateTime from, DateTime to);
}

public interface ISolarPanels
{
    Task<IReadOnlyList<NumericHistoryEntry>> GetTotalSolarPanelPowerHistoryEntriesAsync(DateTime from, DateTime to);
}