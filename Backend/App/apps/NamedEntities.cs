using HomeAssistantGenerated;
using NetDaemon.HassModel.Entities;
using System.Collections.Generic;
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

public interface ICustomButton
{
    IObservable<Event<ZhaEventData>> SinglePressed();

    IObservable<Event<ZhaEventData>> DoublePressed();

    IObservable<Event<ZhaEventData>> LongPressed();
}

public class SonoffButton : ICustomButton
{
    private readonly IHaContext _ha;
    public string DeviceIeee { get; }

    public SonoffButton(IHaContext ha, string deviceIeee)
    {
        _ha = ha;
        DeviceIeee = deviceIeee;
    }

    public IObservable<Event<ZhaEventData>> SinglePressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => string.Equals(e.Data?.DeviceIeee, DeviceIeee, StringComparison.CurrentCultureIgnoreCase) && e.Data?.Command == "toggle");
    }

    public IObservable<Event<ZhaEventData>> DoublePressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => string.Equals(e.Data?.DeviceIeee, DeviceIeee, StringComparison.CurrentCultureIgnoreCase) && e.Data?.Command == "on");
    }

    public IObservable<Event<ZhaEventData>> LongPressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => string.Equals(e.Data?.DeviceIeee, DeviceIeee, StringComparison.CurrentCultureIgnoreCase) && e.Data?.Command == "off");
    }
}

public class SonoffButtonGroup : ICustomButton
{
    private readonly List<SonoffButton> _buttons = [];
    private readonly IHaContext _ha;

    public SonoffButtonGroup(IHaContext ha, params SonoffButton[] buttons)
    {
        _buttons.AddRange([.. buttons]);
        _ha = ha;
    }

    public IObservable<Event<ZhaEventData>> SinglePressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => _buttons.Any(b => string.Equals(e.Data?.DeviceIeee, b.DeviceIeee, StringComparison.CurrentCultureIgnoreCase)) && e.Data?.Command == "toggle");
    }

    public IObservable<Event<ZhaEventData>> DoublePressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => _buttons.Any(b => string.Equals(e.Data?.DeviceIeee, b.DeviceIeee, StringComparison.CurrentCultureIgnoreCase)) && e.Data?.Command == "on");
    }

    public IObservable<Event<ZhaEventData>> LongPressed()
    {
        return _ha.Events.Filter<ZhaEventData>("zha_event").Where(e => _buttons.Any(b => string.Equals(e.Data?.DeviceIeee, b.DeviceIeee, StringComparison.CurrentCultureIgnoreCase)) && e.Data?.Command == "off");
    }
}

public interface INamedEntities
{
    // Dining room
    ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower { get; }
    ICustomNumericSensorEntity DiningRoomClimateHumidity { get; }
    ICustomNumericSensorEntity DiningRoomClimateTemperature { get; }
    ICustomSwitchEntity DiningRoomDeskPlugOnOff { get; }
    ICustomNumericSensorEntity DiningRoomDeskPlugPower { get; }
    ICustomSwitchEntity DiningRoomHeaterSmartPlugOnOff { get; }

    // Kitchen
    ICustomNumericSensorEntity KitchenTemperature { get; }
    ICustomSwitchEntity KitchenHeaterSmartPlugOnOff { get; }

    // Living Room
    ICustomClimateControlEntity LivingRoomRadiatorThermostat { get; }
    ICustomNumericSensorEntity LivingRoomClimateHumidity { get; }
    ICustomNumericSensorEntity LivingRoomClimateTemperature { get; }

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
    public SonoffButton LivingRoomChristmasTreeButton => new(_ha, "C4:D8:C8:FF:FE:49:3E:DF");
    public ICustomSwitchEntity LivingRoomChristmasTreePlugOnOff => new CustomSwitchEntity(_entities.Switch.ChristmasTree);
    public ICustomClimateControlEntity LivingRoomRadiatorThermostat => new CustomClimateControlEntity(_entities.Climate.LivingRoomRadiatorThermostat);
    public ICustomNumericSensorEntity LivingRoomClimateHumidity => new CustomNumericSensorEntity(_entities.Sensor.LivingroomClimateHumidity);
    public ICustomNumericSensorEntity LivingRoomClimateTemperature => new CustomNumericSensorEntity(_entities.Sensor.LivingroomClimateTemperature);

    // Dining room
    public SonoffButton DiningRoomDeskButton => new(_ha, "d4:48:67:ff:fe:0b:f6:0b");
    public InputNumberEntity DiningRoomDehumidierLowerThreshold => _entities.InputNumber.DiningroomDehumidifierLowerThreshold;
    public InputNumberEntity DiningRoomDehumidierUpperThreshold => _entities.InputNumber.DiningroomDehumidifierUpperThreshold;
    public InputNumberEntity DiningRoomDehumidierLookAheadMinutes => _entities.InputNumber.DiningroomDehumidifierProjectionLookAheadMinutes;
    public ICustomSwitchEntity DiningRoomDehumidierSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.SonoffS60zbtpg);
    public ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower => new CustomNumericSensorEntity(_entities.Sensor.SonoffS60zbtpgPower);
    public ICustomNumericSensorEntity DiningRoomClimateHumidity => new CustomNumericSensorEntity(_entities.Sensor.DiningroomHumidity);
    public ICustomNumericSensorEntity DiningRoomClimateTemperature => new CustomNumericSensorEntity(_entities.Sensor.DiningroomTemperature);
    public ICustomSwitchEntity DiningRoomDeskPlugOnOff => new CustomSwitchEntity(_entities.Switch.DiningRoomDeskPlug);
    public ICustomNumericSensorEntity DiningRoomDeskPlugPower => new CustomNumericSensorEntity(_entities.Sensor.DiningRoomDeskPlugPower);
    public ICustomSwitchEntity DiningRoomHeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.DiningRoomPlugHeaterSwitch);
    public SonoffButton DiningRoomBookshelfButton => new(_ha, "d4:48:67:ff:fe:08:1a:bc");
    public LightEntity DiningBookshelfLightStrip => _entities.Light.BookcaseLightStrip;
    public ICustomSwitchEntity DiningBookshelfLightStripPlugOnOff => new CustomSwitchEntity(_entities.Switch.Smartplug01Switch);
    public ICustomSwitchEntity DiningRoomLegoVillage => new CustomSwitchEntity(_entities.Switch.LegoVillage);

    // Kitchen
    public ICustomNumericSensorEntity KitchenTemperature => new CustomNumericSensorEntity(_entities.Sensor.KitchenTemperatureAndHumidityTemperature);
    public ICustomSwitchEntity KitchenHeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.KitchenPlugHeaterSwitch);
    public BinarySensorEntity KitchenMotionSensor => _entities.BinarySensor.MotionSensor02Occupancy;

    // Games room
    public LightEntity GamesRoomDeskLamp => _entities.Light.WizRgbwTunable22099a;
    public ICustomNumericSensorEntity GamesRoomDeskTemperature => new CustomNumericSensorEntity(_entities.Sensor.SonoffSnzb02dTemperature);
    public ICustomNumericSensorEntity GamesRoomDeskHumidity => new CustomNumericSensorEntity(_entities.Sensor.SonoffSnzb02dHumidity);
    public MediaPlayerEntity GamesRoomSpeaker => _entities.MediaPlayer.KitchenSpeaker;
    public ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.GamesRoomPlugHeaterSwitch);
    public ICustomSwitchEntity GamesRoomDeskPlugOnOff => new CustomSwitchEntity(_entities.Switch.GamesRoomDeskPlug);
    public ICustomNumericSensorEntity GamesRoomDeskPlugPower => new CustomNumericSensorEntity(_entities.Sensor.GamesRoomDeskPlugPower);
    public SonoffButton GamesRoomDeskButton => new(_ha, "D4:48:67:FF:FE:0C:12:00");

    // Bedroom 1
    public ICustomNumericSensorEntity Bedroom1Temperature => new CustomNumericSensorEntity(_entities.Sensor.ClockTemperatureAndHumidityTemperature);
    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff => new CustomSwitchEntity(_entities.Switch.Bedroom1PlugHeaterSwitch);

    // Bedroom 2
    public LightEntity BedroomTwoDeskLamp => _entities.Light.LamperionBaneOfShadows;

    // Bedroom 3

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
    public enum FavouriteColour
    {
        None = -1,
        Blue = 0,
        Purple = 1,
        Pink = 2,
        Red = 3,
        Orange = 4,
        Peach = 5,
        Cream = 6,
        White = 7
    }

    public static FavouriteColour GetFavouriteColour(this LightEntity lightEntity)
    {
        if (lightEntity?.Attributes?.RgbColor == null)
        {
            return FavouriteColour.None;
        }

        for (int i = 0; i <= 255; i++)
        {
            (int r, int g, int b) = GetRgb((FavouriteColour)i);
            if (r + g + b == 0)
            {
                break;
            }

            if ((lightEntity.Attributes.RgbColor[0] >= r - 5 && lightEntity.Attributes.RgbColor[0] <= r + 5)
                && (lightEntity.Attributes.RgbColor[1] >= g - 5 && lightEntity.Attributes.RgbColor[1] <= g + 5)
                && (lightEntity.Attributes.RgbColor[2] >= b - 5 && lightEntity.Attributes.RgbColor[2] <= b + 5))
            {
                return (FavouriteColour)i;
            }
        }

        return FavouriteColour.None;
    }

    public static LightEntity SetColour(this LightEntity lightEntity, FavouriteColour colour)
    {
        (int r, int g, int b) = GetRgb(colour);
        lightEntity.TurnOn(rgbColor: [r, g, b]);
        return lightEntity;
    }

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

    private static (int r, int g, int b) GetRgb(FavouriteColour colour)
    {
        return colour switch
        {
            FavouriteColour.Blue => (129, 173, 255),
            FavouriteColour.Purple => (215, 151, 255),
            FavouriteColour.Pink => (255, 159, 242),
            FavouriteColour.Red => (255, 112, 86),
            FavouriteColour.Orange => (255, 137, 14),
            FavouriteColour.Peach => (255, 193, 142),
            FavouriteColour.Cream => (255, 229, 207),
            FavouriteColour.White => (255, 255, 251),
            _ => (0, 0, 0)
        };
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

public interface ICustomClimateControlEntity : ICustomEntity<ICustomClimateControlEntity>
{
    void SetTargetTemperature(double temperature);
    double? CurrentTemperature { get; }
    double? TargetTemperature { get; }
}

public class CustomClimateControlEntity : ICustomClimateControlEntity
{
    private readonly ClimateEntity _climateEntity;

    public string EntityId => _climateEntity.EntityId;

    public double? CurrentTemperature => _climateEntity.Attributes?.CurrentTemperature;

    public double? TargetTemperature => _climateEntity.Attributes?.Temperature;

    public CustomClimateControlEntity(ClimateEntity climateEntity)
    {
        _climateEntity = climateEntity;
    }

    public void SubscribeToStateChangesAsync(Func<ICustomClimateControlEntity, Task> observer)
    {
        _climateEntity.StateChanges().SubscribeAsync(async value => await observer(this));
    }

    public void SetTargetTemperature(double temperature)
    {
        _climateEntity.SetTemperature(temperature);
    }
}