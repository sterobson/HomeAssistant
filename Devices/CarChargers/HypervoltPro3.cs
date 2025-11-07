using HomeAssistant.Devices.Meters;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.CarChargers;

internal class HypervoltPro3 : ICarCharger
{
    private readonly NumericSensorEntity _chargerCurrentSensor;

    public double? ChargerCurrent => _chargerCurrentSensor?.State;

    public HypervoltPro3(IHaContext ha)
    {
        Entities entities = new(ha);

        _chargerCurrentSensor = entities.Sensor.HypervoltChargerCurrent;
    }

    public void OnChargerCurrentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _chargerCurrentSensor.StateChanges().SubscribeAsync(async (value) =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, _chargerCurrentSensor);
            await action(valueChange);
        });
    }
}
