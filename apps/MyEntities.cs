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

    // Hallway
    public SonoffButton HallwayButton => new(_ha, "d4:48:67:ff:fe:08:1a:bc");

    // Porch
    public LightEntity PorchLight => _entities.Light.PorchLight;
    public BinarySensorEntity PorchMotionSensor => _entities.BinarySensor.EwelinkSnzb03pOccupancy;

    // Car
    private Mg4Ev? _car = null;
    public Mg4Ev Car
    {
        get
        {
            _car ??= new Mg4Ev(_entities);
            return _car;
        }
    }
}

//public static class GamesRoomDeskButtonEntityExtensions
//{
//    public static IObservable<Event<ZhaEventData>> DiningRoomDeskButtonToggleStateChanged(this ButtonEntities buttonEntities, IHaContext ha)
//    {
//        return ha.Events.Filter<ZhaEventData>("zha_event").Where(e => e.Data?.DeviceIeee == "d4:48:67:ff:fe:0b:f6:0b" && e.Data?.Command == "toggle");
//    }
//}

//public static class HallwayButtonEntityExtensions
//{
//    public static IObservable<Event<ZhaEventData>> DiningRoomDeskButtonToggleStateChanged(this ButtonEntities buttonEntities, IHaContext ha)
//    {
//        return ha.Events.Filter<ZhaEventData>("zha_event").Where(e => e.Data?.Command == "toggle");
//    }
//}

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

public class Mg4Ev
{
    private readonly Entities _entities;

    public BinarySensorEntity IgnitionEntity => _entities.BinarySensor.MgMg4ElectricEngineStatus;
    public SwitchEntity FrontDefrostEntity => _entities.Switch.MgMg4ElectricFrontDefrost;
    public SwitchEntity RearDefrostEntity => _entities.Switch.MgMg4ElectricRearWindowDefrost;

    public bool? IgnitionState => IgnitionEntity.IsOn;

    public Mg4Ev(Entities entities)
    {
        _entities = entities;
    }

    public void SetFrontDefrost(bool value)
    {
        if (FrontDefrostEntity.State == BinarySensorEntity.StateOff && value)
        {
            FrontDefrostEntity.TurnOn();
        }
        else if (FrontDefrostEntity.State == BinarySensorEntity.StateOn && !value)
        {
            FrontDefrostEntity.TurnOff();
        }
    }

    public void SetRearDefrost(bool value)
    {
        if (RearDefrostEntity.State == BinarySensorEntity.StateOff && value)
        {
            RearDefrostEntity.TurnOn();
        }
        else if (RearDefrostEntity.State == BinarySensorEntity.StateOn && !value)
        {
            RearDefrostEntity.TurnOff();
        }
    }
}
