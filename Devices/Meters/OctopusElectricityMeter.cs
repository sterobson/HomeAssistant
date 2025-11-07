using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Meters;

internal class OctopusElectricityMeter : IElectricityMeter
{
    private readonly IHaContext _ha;
    private readonly NumericSensorEntity _currentRateSensor;

    public double? CurrentRatePerKwh => _currentRateSensor.State;

    public OctopusElectricityMeter(IHaContext ha)
    {
        Entities entities = new(ha);
        _ha = ha;
        _currentRateSensor = entities.Sensor.OctopusEnergyElectricity24j04946911591015382045CurrentRate;
    }

    public void OnCurrentRatePerKwhChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _currentRateSensor.StateChanges().SubscribeAsyncConcurrent(async (value) =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, _currentRateSensor);
            await action(valueChange);
        });
    }
}
