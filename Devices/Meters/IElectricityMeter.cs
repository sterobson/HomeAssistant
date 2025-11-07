using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.Meters;

public record ValueChange<TValue, TEntity>(TValue Old, TValue New, TEntity Entity);

internal interface IElectricityMeter
{
    double? CurrentRatePerKwh { get; }

    void OnCurrentRatePerKwhChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action);
}
