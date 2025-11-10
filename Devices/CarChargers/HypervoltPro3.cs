using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Devices.CarChargers;

internal class HypervoltPro3 : ICarCharger
{
    private readonly NumericSensorEntity _chargerCurrentSensor;
    private readonly HistoryService _historyService;

    public double? ChargerCurrent => _chargerCurrentSensor.State;

    public HypervoltPro3(IHaContext ha, HistoryService historyService)
    {
        Entities entities = new(ha);

        _chargerCurrentSensor = entities.Sensor.HypervoltChargerCurrent;
        _historyService = historyService;
    }

    public void OnChargerCurrentChanged(Func<ValueChange<double?, NumericSensorEntity>, Task> action)
    {
        _chargerCurrentSensor.StateChanges().Subscribe(async value =>
        {
            ValueChange<double?, NumericSensorEntity> valueChange = new(value.Old?.State, value.New?.State, _chargerCurrentSensor);
            await action(valueChange);
        });
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetChargerCurrentHistoryEntriesAsync(DateTime from, DateTime to)
    {
        IEnumerable<HistoryEntry> results = (await _historyService.GetEntityHistory(_chargerCurrentSensor.EntityId, from)).Where(h => h.LastChanged >= from && h.LastChanged <= to);
        return [.. results];
    }
}
