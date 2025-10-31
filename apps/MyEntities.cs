using HomeAssistantGenerated;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.apps;

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

    // Testing
    public InputBooleanEntity AutomationTest => _entities.InputBoolean.AutomationTest;

    // Living room

    // Dining room
    public SonoffButton DiningRoomDeskButton => new(_ha, "d4:48:67:ff:fe:0b:f6:0b");

    // Kitchen

    // Games room
    public LightEntity GamesRoomDeskLamp => _entities.Light.WizRgbwTunable22099a;
    public NumericSensorEntity GamesRoomDeskTemperature => _entities.Sensor.SonoffSnzb02dTemperature;
    public NumericSensorEntity GamesRoomDeskHumidity => _entities.Sensor.SonoffSnzb02dHumidity;
    public MediaPlayerEntity GamesRoomSpeaker => _entities.MediaPlayer.KitchenSpeaker;

    // Bedroom 1


    // Bedroom 2
    public LightEntity BedroomTwoDeskLamp => _entities.Light.LamperionBaneOfShadows;

    // Bedroom 3

    // Porch
    public LightEntity PorchLight => _entities.Light.PorchLight;
    public BinarySensorEntity PorchMotionSensor => _entities.BinarySensor.EwelinkSnzb03pOccupancy;
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

    public static LightEntity SetRgb(this LightEntity lightEntity, int r, int g, int b, long brightnessPercent)
    {
        lightEntity.TurnOn(rgbColor: [r, g, b], brightnessPct: brightnessPercent);
        return lightEntity;
    }

    public static LightEntity SetBrightnessPercent(this LightEntity lightEntity, long? brightnessPercent)
    {
        lightEntity.TurnOn(brightnessPct: brightnessPercent);
        return lightEntity;
    }

}
