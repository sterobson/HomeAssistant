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
    public Prices(IHaContext ha, ITriggerManager triggerManager)
    {
        Entities entities = new(ha);

        entities.Sensor.OctopusEnergyElectricity24j04946911591015382045CurrentRate.StateAllChanges().SubscribeAsync(async e =>
        {
            Console.WriteLine($"{DateTime.UtcNow}: From {e.Old?.State} to {e.New?.State}");
        });

    }
}