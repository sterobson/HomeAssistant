using HomeAssistantGenerated;
using NetDaemon.HassModel.Entities;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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

public interface INamedEntities
{
    // Dining room
    ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower { get; }
    ICustomNumericSensorEntity DiningRoomClimateHumidity { get; }
    ICustomNumericSensorEntity DiningRoomDeskPlugPower { get; }

    // Kitchen
    ICustomNumericSensorEntity KitchenTemperature { get; }
    ICustomSwitchEntity KitchenHeaterSmartPlugOnOff { get; }

    // Games room
    ICustomNumericSensorEntity GamesRoomDeskTemperature { get; }
    ICustomNumericSensorEntity GamesRoomDeskHumidity { get; }
    ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff { get; }
    ICustomSwitchEntity GamesRoomDeskPlugOnOff { get; }
    ICustomNumericSensorEntity GamesRoomDeskPlugPower { get; }

    // Bedroom 1
    ICustomNumericSensorEntity Bedroom1Temperature { get; }
    ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff { get; }

}

public class NamedEntities : INamedEntities
{
    private readonly Entities _entities;
    private readonly IHaContext _ha;

    public NamedEntities(IHaContext ha)
    {
        _entities = new Entities(ha);
        _ha = ha;
    }

    // Testing
    public InputBooleanEntity AutomationTest => _entities.InputBoolean.AutomationTest;

    // Living room

    // Dining room
    public SonoffButton DiningRoomDeskButton => new(_ha, "d4:48:67:ff:fe:0b:f6:0b");
    public InputNumberEntity DiningRoomDehumidierLowerThreshold => _entities.InputNumber.DiningroomDehumidifierLowerThreshold;
    public InputNumberEntity DiningRoomDehumidierUpperThreshold => _entities.InputNumber.DiningroomDehumidifierUpperThreshold;
    public InputNumberEntity DiningRoomDehumidierLookAheadMinutes => _entities.InputNumber.DiningroomDehumidifierProjectionLookAheadMinutes;
    public ICustomSwitchEntity DiningRoomDehumidierSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.SonoffS60zbtpg);
    public ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower => new CustomNumericSensorEntity(_entities.Sensor.SonoffS60zbtpgPower);
    public ICustomNumericSensorEntity DiningRoomClimateHumidity => new CustomNumericSensorEntity(_entities.Sensor.DiningroomHumidity);
    public ICustomSwitchEntity DiningRoomDeskSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.DiningRoomDeskPlug);
    public ICustomNumericSensorEntity DiningRoomDeskPlugPower => new CustomNumericSensorEntity(_entities.Sensor.DiningRoomDeskPlugPower);

    // Kitchen
    public ICustomNumericSensorEntity KitchenTemperature => new CustomNumericSensorEntity(_entities.Sensor.KitchenTemperatureAndHumidityTemperature);
    public ICustomSwitchEntity KitchenHeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.KitchenPlugHeaterSwitch);

    // Games room
    public LightEntity GamesRoomDeskLamp => _entities.Light.WizRgbwTunable22099a;
    public ICustomNumericSensorEntity GamesRoomDeskTemperature => new CustomNumericSensorEntity(_entities.Sensor.SonoffSnzb02dTemperature);
    public ICustomNumericSensorEntity GamesRoomDeskHumidity => new CustomNumericSensorEntity(_entities.Sensor.SonoffSnzb02dHumidity);
    public MediaPlayerEntity GamesRoomSpeaker => _entities.MediaPlayer.KitchenSpeaker;
    public ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.GamesRoomPlugHeaterSwitch);
    public ICustomSwitchEntity GamesRoomDeskPlugOnOff => new CustomSwitchEntity(_entities.Switch.GamesRoomDeskPlug);
    public ICustomNumericSensorEntity GamesRoomDeskPlugPower => new CustomNumericSensorEntity(_entities.Sensor.GamesRoomDeskPlugPower);

    // Bedroom 1
    public ICustomNumericSensorEntity Bedroom1Temperature => new CustomNumericSensorEntity(_entities.Sensor.ClockTemperatureAndHumidityTemperature);
    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.Bedroom1PlugHeaterSwitch);

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

public static class SwitchEntityExtensions
{
    public static Task SetState(this SwitchEntity switchEntity, bool value)
    {
        if (switchEntity.State == BinarySensorEntity.StateOff && value)
        {
            switchEntity.TurnOn();
        }
        else if (switchEntity.State == BinarySensorEntity.StateOn && !value)
        {
            switchEntity.TurnOff();
        }

        return Task.CompletedTask;
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

public interface ICustomEntity<T>
{
    string EntityId { get; }

    void SubscribeToStateChangesAsync(Func<T, Task> observer);
}

public interface ICustomNumericSensorEntity : ICustomEntity<ICustomNumericSensorEntity>
{
    double? State { get; }
}

public class CustomNumericSensorEntity : ICustomNumericSensorEntity
{
    private readonly NumericSensorEntity _numericSensorEntity;

    public CustomNumericSensorEntity(NumericSensorEntity numericSensorEntity)
    {
        _numericSensorEntity = numericSensorEntity;
    }

    public double? State => _numericSensorEntity.State;
    public string EntityId => _numericSensorEntity.EntityId;

    public void SubscribeToStateChangesAsync(Func<ICustomNumericSensorEntity, Task> observer)
    {
        _numericSensorEntity.StateChanges().SubscribeAsync(async value => await observer(this));
    }
}

public interface ICustomSwitchEntity : ICustomEntity<ICustomSwitchEntity>
{
    bool IsOn();
    bool IsOff();
    void TurnOn();
    void TurnOff();
}

public class CustomSwitchEntity : ICustomSwitchEntity
{
    private readonly SwitchEntity _switchEntity;
    public string EntityId => _switchEntity.EntityId;

    public CustomSwitchEntity(SwitchEntity switchEntity)
    {
        _switchEntity = switchEntity;
    }

    public bool IsOn() => _switchEntity.IsOn();
    public bool IsOff() => _switchEntity.IsOff();

    public void TurnOn()
    {
        if (!_switchEntity.IsOn())
        {
            _switchEntity.TurnOn();
        }
    }

    public void TurnOff()
    {
        if (!_switchEntity.IsOff())
        {
            _switchEntity.TurnOff();
        }
    }

    public void SubscribeToStateChangesAsync(Func<ICustomSwitchEntity, Task> observer)
    {
        _switchEntity.StateChanges().SubscribeAsync(async value => await observer(this));
    }
}