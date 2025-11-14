using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Batteries;

public class SolaxInverter : IHomeBattery, ISolarPanels
{
    private readonly IHaContext _ha;
    private readonly HistoryService _historyService;
    private readonly NumericSensorEntity _batteryChargePercentSensor;
    private readonly SelectEntity _chargerUseMode;
    private readonly SelectEntity _chargerManualMode;
    private readonly NumberEntity _batteryChargeMaxCurrent;
    private readonly NumericSensorEntity _totalBatteryPowerCharge;
    private readonly NumericSensorEntity _totalPvPowerSensor;
    private readonly HomeAssistantGenerated.Services _services;

    public double? CurrentChargePercent => _batteryChargePercentSensor?.State;

    public double BatteryCapacitykWh => 20.4;

    public SolaxInverter(IHaContext ha, HistoryService historyService)
    {
        Entities entities = new(ha);
        _ha = ha;
        _historyService = historyService;
        _services = new(ha);

        _batteryChargePercentSensor = entities.Sensor.SolaxInverterBatteryCapacity;
        _chargerUseMode = entities.Select.SolaxInverterChargerUseMode;
        _chargerManualMode = entities.Select.SolaxInverterManualModeSelect;
        _batteryChargeMaxCurrent = entities.Number.SolaxInverterBatteryChargeMaxCurrent;
        _totalBatteryPowerCharge = entities.Sensor.SolaxInverterTotalBatteryPowerCharge;
        _totalPvPowerSensor = entities.Sensor.SolaxInverterPvPowerTotal;
    }

    public void OnBatteryChargePercentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _batteryChargePercentSensor.StateChanges().SubscribeAsync(async (value) =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, _batteryChargePercentSensor);
            await action(valueChange);
        });
    }

    public BatteryState GetHomeBatteryState()
    {
        string? chargerUseMode = _chargerUseMode.State;
        string? chargerManualMode = _chargerManualMode.State;

        return chargerUseMode switch
        {
            "Smart Schedule" => BatteryState.NormalTOU,
            "Manual Mode" => chargerManualMode switch
            {
                "Stop Charge and Discharge" => BatteryState.Stopped,
                "Force Charge" => BatteryState.ForceCharging,
                "Force Discharge" => BatteryState.ForceDischarging,
                _ => BatteryState.Unknown,
            },
            _ => BatteryState.Unknown,
        };
    }

    public void SetHomeBatteryState(BatteryState desiredHomeBatteryState)
    {
        BatteryState currentHomeBatteryState = GetHomeBatteryState();

        if (desiredHomeBatteryState != currentHomeBatteryState)
        {
            switch (desiredHomeBatteryState)
            {
                case BatteryState.NormalTOU:
                case BatteryState.Unknown:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [_chargerUseMode.EntityId] },
                        option: "Smart Schedule"
                    );
                    break;

                case BatteryState.ForceCharging:
                case BatteryState.ForceDischarging:
                case BatteryState.Stopped:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [_chargerUseMode.EntityId] },
                        option: "Manual Mode"
                    );
                    break;
            }

            switch (desiredHomeBatteryState)
            {
                case BatteryState.ForceCharging:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [_chargerManualMode.EntityId] },
                        option: "Force Charge"
                    );
                    break;
                case BatteryState.ForceDischarging:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [_chargerManualMode.EntityId] },
                        option: "Force Discharge"
                    );
                    break;
                case BatteryState.Stopped:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [_chargerManualMode.EntityId] },
                        option: "Stop Charge and Discharge"
                    );
                    break;
            }
        }
    }

    private const int MaxChargeCurrent = 50;
    public void SetMaxChargeCurrentHeadroom(int headroom)
    {
        _batteryChargeMaxCurrent.SetValue((MaxChargeCurrent - headroom).ToString());
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetTotalBatteryPowerChargeHistoryEntriesAsync(DateTime from, DateTime to)
    {
        return await _historyService.GetEntityHistory(_totalBatteryPowerCharge.EntityId, from, to);
    }

    public async Task<IReadOnlyList<NumericHistoryEntry>> GetTotalSolarPanelPowerHistoryEntriesAsync(DateTime from, DateTime to)
    {
        return await _historyService.GetEntityHistory(_totalPvPowerSensor.EntityId, from, to);
    }
}
