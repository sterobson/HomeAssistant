// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistantGenerated;

namespace HassModel.Energy;

/// <summary>
///     Hello world showcase using the new HassModel API
/// </summary>
[NetDaemonApp]
public class Prices
{
    private readonly ILogger<Prices> _logger;

    public Prices(IHaContext ha, ITriggerManager triggerManager, ILogger<Prices> logger)
    {
        _logger = logger;

        Entities entities = new(ha);

        entities.Sensor.OctopusEnergyElectricity24j04946911591015382045CurrentRate.StateChanges().SubscribeAsync(async e =>
        {
            _logger.LogDebug("Electricity import prices changed from {oldValue} to {newValue} per kWh", e.Old?.State, e.New?.State);
        });
    }
}