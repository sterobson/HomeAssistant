using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.CarChargers;

public interface ICarCharger
{
    double? ChargerCurrent { get; }

    Task<IReadOnlyList<NumericHistoryEntry>> GetChargerCurrentHistoryEntriesAsync(DateTime from, DateTime to);

    void OnChargerCurrentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action);
}