// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.apps.Energy;
using HomeAssistantGenerated;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HassModel;

/// <summary>
///     Hello world showcase using the new HassModel API
/// </summary>
[NetDaemonApp]
public class HelloWorldApp
{
    public HelloWorldApp(IHaContext ha, ITriggerManager triggerManager, IElectricityRatesReader electricityRates)
    {
        ha.CallService("notify", "persistent_notification", data: new { message = "Notify me", title = "Hello world!" });

        var entities = new Entities(ha);
        var myDevices = new MyDevices(entities, ha);

        myDevices.DiningRoomDeskButton.Pressed().SubscribeAsync(async e =>
        {
            int i = 0;
            Random random = new();
            while (i++ < 30)
            {
                myDevices.GamesRoomDeskLamp
                    .SetRgb(random.Next(0, 256), random.Next(0, 256), random.Next(0, 256))
                    .SetBrightnessPercent(50);
                await Task.Delay(100);
            }

            myDevices.GamesRoomDeskLamp.TurnOn(effect: "Warm white");
        });

        myDevices.GamesRoomDeskTemperature.StateChanges().SubscribeAsync(async e =>
        {
            Console.WriteLine($"{e.Entity.Attributes?.FriendlyName} set to {e.New?.State}{e.Entity.Attributes?.UnitOfMeasurement}");
        });

        myDevices.GamesRoomDeskHumidity.StateChanges().SubscribeAsync(async e =>
        {
            Console.WriteLine($"{e.Entity.Attributes?.FriendlyName} set to {e.New?.State}{e.Entity.Attributes?.UnitOfMeasurement}");
        });

        var rate = electricityRates.GetCurrentElectricityImportRateAsync().GetAwaiter().GetResult();
    }

}

public record ZhaEventData
{
    [JsonPropertyName("device_ieee")] public string? DeviceIeee { get; init; }
    [JsonPropertyName("unique_id")] public string? UniqueId { get; init; }
    [JsonPropertyName("endpoint_id")] public int? EndpointId { get; init; }
    [JsonPropertyName("cluster_id")] public int? ClusterId { get; init; }
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("args")] public JsonElement? Args { get; init; }
}


public class SonoffButton
{
    private readonly IHaContext _ha;
    private readonly string _deviceIeee;

    public SonoffButton(IHaContext ha, string deviceIeee)
    {
        _ha = ha;
        _deviceIeee = deviceIeee;
    }

    public IObservable<Event<ZhaEventData>> Pressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => e.Data?.DeviceIeee == _deviceIeee && e.Data?.Command == "toggle");
    }
}

public class MyDevices
{
    private readonly Entities _entities;
    private readonly IHaContext _ha;

    public MyDevices(Entities entities, IHaContext ha)
    {
        _entities = entities;
        _ha = ha;
    }

    // Living room

    // Dining room
    public SonoffButton DiningRoomDeskButton => new(_ha, "d4:48:67:ff:fe:0b:f6:0b");

    // Kitchen

    // Games room
    public LightEntity GamesRoomDeskLamp => _entities.Light.WizRgbwTunable22099a;
    public NumericSensorEntity GamesRoomDeskTemperature => _entities.Sensor.SonoffSnzb02dTemperature;
    public NumericSensorEntity GamesRoomDeskHumidity => _entities.Sensor.SonoffSnzb02dHumidity;

    // Bedroom 1


    // Bedroom 2
    public LightEntity BedroomTwoDeskLamp => _entities.Light.LamperionBaneOfShadows;

    // Bedroom 3
}

public static class GamesRoomDeskButtonEntityExtensions
{
    public static IObservable<Event<ZhaEventData>> DiningRoomDeskButtonToggleStateChanged(this ButtonEntities buttonEntities, IHaContext ha)
    {
        return ha.Events.Filter<ZhaEventData>("zha_event").Where(e => e.Data?.DeviceIeee == "d4:48:67:ff:fe:0b:f6:0b" && e.Data?.Command == "toggle");
    }
}

public static class LightEntityExtensions
{
    public static LightEntity SetRgb(this LightEntity lightEntity, int r, int g, int b)
    {
        lightEntity.TurnOn(rgbColor: [r, g, b]);
        return lightEntity;
    }

    public static LightEntity SetBrightnessPercent(this LightEntity lightEntity, long? brightnesPercent)
    {
        lightEntity.TurnOn(brightnessPct: brightnesPercent);
        return lightEntity;
    }

}