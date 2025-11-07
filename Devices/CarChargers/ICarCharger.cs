using HomeAssistant.Devices.Meters;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.CarChargers;

public interface ICarCharger
{
    double? ChargerCurrent { get; }

    void OnChargerCurrentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action);
}