using HomeAssistant.Devices.Meters;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Batteries;

public class SolaxInverter : IHomeBattery
{
    private readonly IHaContext _ha;
    private readonly NumericSensorEntity _batteryCapacitySensor;
    private readonly SelectEntity _chargerUseMode;
    private readonly SelectEntity _chargerManualMode;
    private readonly NumberEntity _batteryChargeMaxCurrent;
    private readonly HomeAssistantGenerated.Services _services;

    public double? CurrentChargePercent => _batteryCapacitySensor?.State;

    public SolaxInverter(IHaContext ha)
    {
        Entities entities = new(ha);
        _ha = ha;
        _services = new(ha);

        _batteryCapacitySensor = entities.Sensor.SolaxInverterBatteryCapacity;
        _chargerUseMode = entities.Select.SolaxInverterChargerUseMode;
        _chargerManualMode = entities.Select.SolaxInverterManualModeSelect;
        _batteryChargeMaxCurrent = entities.Number.SolaxInverterBatteryChargeMaxCurrent;
    }

    public void OnBatteryChargePercentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _batteryCapacitySensor.StateChanges().SubscribeAsync(async (value) =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, _batteryCapacitySensor);
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
        _batteryChargeMaxCurrent.SetValue((50 - headroom).ToString());

    }
}
