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
    ICustomClimateControlEntity DiningRoomRadiatorThermostat { get; }

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
    ICustomClimateControlEntity Bedroom1RadiatorThermostat { get; }

    // Bedroom 2
    ICustomNumericSensorEntity Bedroom2Temperature { get; }
    ICustomClimateControlEntity Bedroom2RadiatorThermostat { get; }

    // Bedroom 3
    ICustomNumericSensorEntity Bedroom3Temperature { get; }
    ICustomClimateControlEntity Bedroom3RadiatorThermostat { get; }

    // Upstairs bathroom
    ICustomNumericSensorEntity UpstairsBathroomTemperature { get; }
}

public class NamedEntities : INamedEntities
{
    private readonly IHaContext _ha;

    private readonly ICustomSwitchEntity _gamesRoomHeaterSmartPlugOnOff;
    private readonly ICustomSwitchEntity _gamesRoomDeskPlugOnOff;
    private readonly ICustomNumericSensorEntity _gamesRoomDeskPlugPower;

    private readonly ICustomSwitchEntity _diningRoomHeaterSmartPlugOnOff;
    private readonly ICustomSwitchEntity _diningRoomDeskPlugOnOff;
    private readonly ICustomNumericSensorEntity _diningRoomDeskPlugPower;

    private readonly ICustomSwitchEntity _bedroom1HeaterSmartPlugOnOff;

    private readonly ICustomSwitchEntity _kitchenHeaterSmartPlugOnOff;

    // Testing
    private readonly InputBooleanEntity _automationTest;

    // Living room
    private readonly SonoffButton _livingRoomChristmasTreeButton;
    private readonly ICustomSwitchEntity _livingRoomChristmasTreePlugOnOff;
    private readonly ICustomClimateControlEntity _livingRoomRadiatorThermostat;
    private readonly ICustomNumericSensorEntity _livingRoomClimateHumidity;
    private readonly ICustomNumericSensorEntity _livingRoomClimateTemperature;

    // Dining room
    private readonly SonoffButton _diningRoomDeskButton;
    private readonly InputNumberEntity _diningRoomDehumidierLowerThreshold;
    private readonly InputNumberEntity _diningRoomDehumidierUpperThreshold;
    private readonly InputNumberEntity _diningRoomDehumidierLookAheadMinutes;
    private readonly ICustomSwitchEntity _diningRoomDehumidierSmartPlugOnOff;
    private readonly ICustomNumericSensorEntity _diningRoomDehumidifierSmartPlugPower;
    private readonly ICustomNumericSensorEntity _diningRoomClimateHumidity;
    private readonly ICustomNumericSensorEntity _diningRoomClimateTemperature;
    private readonly SonoffButton _diningRoomBookshelfButton;
    private readonly LightEntity _diningBookshelfLightStrip;
    private readonly ICustomSwitchEntity _diningBookshelfLightStripPlugOnOff;
    private readonly ICustomSwitchEntity _diningRoomLegoVillage;
    private readonly ICustomClimateControlEntity _diningRoomRadiatorThermostat;

    // Kitchen
    private readonly ICustomNumericSensorEntity _kitchenTemperature;
    private readonly BinarySensorEntity _kitchenMotionSensor;

    // Games room
    private readonly LightEntity _gamesRoomDeskLamp;
    private readonly ICustomNumericSensorEntity _gamesRoomDeskTemperature;
    private readonly ICustomNumericSensorEntity _gamesRoomDeskHumidity;
    private readonly MediaPlayerEntity _gamesRoomSpeaker;
    private readonly SonoffButton _gamesRoomDeskButton;

    // Bedroom 1
    private readonly ICustomNumericSensorEntity _bedroom1Temperature;
    private readonly ICustomClimateControlEntity _bedroom1RadiatorThermostat;

    // Bedroom 2
    private readonly LightEntity _bedroomTwoDeskLamp;
    private readonly ICustomNumericSensorEntity _bedroom2Temperature;
    private readonly ICustomClimateControlEntity _bedroom2RadiatorThermostat;

    // Bedroom 3
    private readonly ICustomNumericSensorEntity _bedroom3Temperature;
    private readonly ICustomClimateControlEntity _bedroom3RadiatorThermostat;

    // Upstairs bathroom
    private readonly ICustomNumericSensorEntity _upstairsBathroomTemperature;

    // Porch
    private readonly LightEntity _porchLight;
    private readonly BinarySensorEntity _porchMotionSensor;

    public NamedEntities(IHaContext ha)
    {
        _ha = ha;

        _gamesRoomDeskPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.games_room_desk_plug"));
        _gamesRoomDeskPlugPower = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.games_room_desk_plug_power"));
        _gamesRoomHeaterSmartPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.games_room_plug_heater_switch"));

        _diningRoomDeskPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.dining_room_desk_plug"));
        _diningRoomDeskPlugPower = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.dining_room_desk_plug_power"));
        _diningRoomHeaterSmartPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.dining_room_plug_heater_switch"));

        _bedroom1HeaterSmartPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.bedroom_1_plug_heater_switch"));

        _kitchenHeaterSmartPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.kitchen_plug_heater_switch"));

        // Testing
        _automationTest = new InputBooleanEntity(ha, "input_boolean.automation_test");

        // Living room
        _livingRoomChristmasTreeButton = new SonoffButton(ha, "C4:D8:C8:FF:FE:49:3E:DF");
        _livingRoomChristmasTreePlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.christmas_tree"));
        _livingRoomRadiatorThermostat = new CustomClimateControlEntity(new ClimateEntity(ha, "climate.living_room_radiator_thermostat"));
        _livingRoomClimateHumidity = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.livingroom_climate_humidity"));
        _livingRoomClimateTemperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.livingroom_climate_temperature"));

        // Dining room
        _diningRoomDeskButton = new SonoffButton(ha, "d4:48:67:ff:fe:0b:f6:0b");
        _diningRoomDehumidierLowerThreshold = new InputNumberEntity(ha, "input_number.diningroom_dehumidifier_lower_threshold");
        _diningRoomDehumidierUpperThreshold = new InputNumberEntity(ha, "input_number.diningroom_dehumidifier_upper_threshold");
        _diningRoomDehumidierLookAheadMinutes = new InputNumberEntity(ha, "input_number.diningroom_dehumidifier_projection_look_ahead_minutes");
        _diningRoomDehumidierSmartPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.sonoff_s60zbtpg"));
        _diningRoomDehumidifierSmartPlugPower = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.sonoff_s60zbtpg_power"));
        _diningRoomClimateHumidity = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.diningroom_humidity"));
        _diningRoomClimateTemperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.diningroom_temperature"));
        _diningRoomBookshelfButton = new SonoffButton(ha, "d4:48:67:ff:fe:08:1a:bc");
        _diningBookshelfLightStrip = new LightEntity(ha, "light.bookcase_light_strip");
        _diningBookshelfLightStripPlugOnOff = new CustomSwitchEntity(new SwitchEntity(ha, "switch.smartplug01_switch"));
        _diningRoomLegoVillage = new CustomSwitchEntity(new SwitchEntity(ha, "switch.lego_village"));
        _diningRoomRadiatorThermostat = new CustomClimateControlEntity(new ClimateEntity(ha, "climate.dining_room_radiator_thermostat"));

        // Kitchen
        _kitchenTemperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.kitchen_temperature_and_humidity_temperature"));
        _kitchenMotionSensor = new BinarySensorEntity(ha, "binary_sensor.motion_sensor_02_occupancy");

        // Games room
        _gamesRoomDeskLamp = new LightEntity(ha, "light.wiz_rgbw_tunable_22099a");
        _gamesRoomDeskTemperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.sonoff_snzb_02d_temperature"));
        _gamesRoomDeskHumidity = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.sonoff_snzb_02d_humidity"));
        _gamesRoomSpeaker = new MediaPlayerEntity(ha, "media_player.kitchen_speaker");
        _gamesRoomDeskButton = new SonoffButton(ha, "D4:48:67:FF:FE:0C:12:00");

        // Bedroom 1
        _bedroom1Temperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.clock_temperature_and_humidity_temperature"));
        _bedroom1RadiatorThermostat = new CustomClimateControlEntity(new ClimateEntity(ha, "climate.bedroom_1_radiator_thermostat"));

        // Bedroom 2
        _bedroomTwoDeskLamp = new LightEntity(ha, "light.lamperion_bane_of_shadows");
        _bedroom2Temperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.bedroom_2_climate_temperature"));
        _bedroom2RadiatorThermostat = new CustomClimateControlEntity(new ClimateEntity(ha, "climate.bedroom_2_radiator_thermostat"));

        // Bedroom 3
        _bedroom3Temperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.bedroom_3_climate_temperature"));
        _bedroom3RadiatorThermostat = new CustomClimateControlEntity(new ClimateEntity(ha, "climate.bedroom_3_radiator_thermostat"));

        // Upstairs bathroom
        _upstairsBathroomTemperature = new CustomNumericSensorEntity(new NumericSensorEntity(ha, "sensor.bathroom_temperature"));

        // Porch
        _porchLight = new LightEntity(ha, "light.porch_light");
        _porchMotionSensor = new BinarySensorEntity(ha, "binary_sensor.ewelink_snzb_03p_occupancy");
    }

    // Testing
    public InputBooleanEntity AutomationTest => _automationTest;

    // Living room
    public SonoffButton LivingRoomChristmasTreeButton => _livingRoomChristmasTreeButton;
    public ICustomSwitchEntity LivingRoomChristmasTreePlugOnOff => _livingRoomChristmasTreePlugOnOff;
    public ICustomClimateControlEntity LivingRoomRadiatorThermostat => _livingRoomRadiatorThermostat;
    public ICustomNumericSensorEntity LivingRoomClimateHumidity => _livingRoomClimateHumidity;
    public ICustomNumericSensorEntity LivingRoomClimateTemperature => _livingRoomClimateTemperature;

    // Dining room
    public SonoffButton DiningRoomDeskButton => _diningRoomDeskButton;
    public InputNumberEntity DiningRoomDehumidierLowerThreshold => _diningRoomDehumidierLowerThreshold;
    public InputNumberEntity DiningRoomDehumidierUpperThreshold => _diningRoomDehumidierUpperThreshold;
    public InputNumberEntity DiningRoomDehumidierLookAheadMinutes => _diningRoomDehumidierLookAheadMinutes;
    public ICustomSwitchEntity DiningRoomDehumidierSmartPlugOnOff => _diningRoomDehumidierSmartPlugOnOff;
    public ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower => _diningRoomDehumidifierSmartPlugPower;
    public ICustomNumericSensorEntity DiningRoomClimateHumidity => _diningRoomClimateHumidity;
    public ICustomNumericSensorEntity DiningRoomClimateTemperature => _diningRoomClimateTemperature;
    public ICustomSwitchEntity DiningRoomDeskPlugOnOff => _diningRoomDeskPlugOnOff;
    public ICustomNumericSensorEntity DiningRoomDeskPlugPower => _diningRoomDeskPlugPower;
    public ICustomSwitchEntity DiningRoomHeaterSmartPlugOnOff => _diningRoomHeaterSmartPlugOnOff;
    public SonoffButton DiningRoomBookshelfButton => _diningRoomBookshelfButton;
    public LightEntity DiningBookshelfLightStrip => _diningBookshelfLightStrip;
    public ICustomSwitchEntity DiningBookshelfLightStripPlugOnOff => _diningBookshelfLightStripPlugOnOff;
    public ICustomSwitchEntity DiningRoomLegoVillage => _diningRoomLegoVillage;
    public ICustomClimateControlEntity DiningRoomRadiatorThermostat => _diningRoomRadiatorThermostat;

    // Kitchen
    public ICustomNumericSensorEntity KitchenTemperature => _kitchenTemperature;
    public ICustomSwitchEntity KitchenHeaterSmartPlugOnOff => _kitchenHeaterSmartPlugOnOff;
    public BinarySensorEntity KitchenMotionSensor => _kitchenMotionSensor;

    // Games room
    public LightEntity GamesRoomDeskLamp => _gamesRoomDeskLamp;
    public ICustomNumericSensorEntity GamesRoomDeskTemperature => _gamesRoomDeskTemperature;
    public ICustomNumericSensorEntity GamesRoomDeskHumidity => _gamesRoomDeskHumidity;
    public MediaPlayerEntity GamesRoomSpeaker => _gamesRoomSpeaker;
    public ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff => _gamesRoomHeaterSmartPlugOnOff;
    public ICustomSwitchEntity GamesRoomDeskPlugOnOff => _gamesRoomDeskPlugOnOff;
    public ICustomNumericSensorEntity GamesRoomDeskPlugPower => _gamesRoomDeskPlugPower;
    public SonoffButton GamesRoomDeskButton => _gamesRoomDeskButton;

    // Bedroom 1
    public ICustomNumericSensorEntity Bedroom1Temperature => _bedroom1Temperature;
    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff => _bedroom1HeaterSmartPlugOnOff;
    public ICustomClimateControlEntity Bedroom1RadiatorThermostat => _bedroom1RadiatorThermostat;

    // Bedroom 2
    public LightEntity BedroomTwoDeskLamp => _bedroomTwoDeskLamp;
    public ICustomNumericSensorEntity Bedroom2Temperature => _bedroom2Temperature;
    public ICustomClimateControlEntity Bedroom2RadiatorThermostat => _bedroom2RadiatorThermostat;

    // Bedroom 3
    public ICustomNumericSensorEntity Bedroom3Temperature => _bedroom3Temperature;
    public ICustomClimateControlEntity Bedroom3RadiatorThermostat => _bedroom3RadiatorThermostat;

    // Upstairs bathroom
    public ICustomNumericSensorEntity UpstairsBathroomTemperature => _upstairsBathroomTemperature;

    // Porch
    public LightEntity PorchLight => _porchLight;
    public BinarySensorEntity PorchMotionSensor => _porchMotionSensor;

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

public class HousePresenceSensor : ICustomBooleanSensorEntity
{
    private readonly ICustomSwitchEntity _gamesRoomDeskSwitch;
    private readonly ICustomNumericSensorEntity _gamesRoomDeskPlugPower;
    private readonly ICustomSwitchEntity _diningRoomDeskSwitch;
    private readonly ICustomNumericSensorEntity _diningRoomDeskPlugPower;

    public string EntityId => nameof(HousePresenceSensor);

    public bool? State => (_gamesRoomDeskSwitch.IsOn() && _gamesRoomDeskPlugPower.State > 30)
        || (_diningRoomDeskSwitch.IsOn() && _diningRoomDeskPlugPower.State > 30);

    public HousePresenceSensor(
        ICustomSwitchEntity gamesRoomDeskSwitch, ICustomNumericSensorEntity gamesRoomDeskPlugPower,
        ICustomSwitchEntity diningRoomDeskSwitch, ICustomNumericSensorEntity diningRoomDeskPlugPower)
    {
        _gamesRoomDeskSwitch = gamesRoomDeskSwitch;
        _gamesRoomDeskPlugPower = gamesRoomDeskPlugPower;
        _diningRoomDeskSwitch = diningRoomDeskSwitch;
        _diningRoomDeskPlugPower = diningRoomDeskPlugPower;
    }

    public void SubscribeToStateChangesAsync(Func<ICustomBooleanSensorEntity, Task> observer)
    {
        _gamesRoomDeskSwitch.SubscribeToStateChangesAsync(async value => await observer(this));
        _gamesRoomDeskPlugPower.SubscribeToStateChangesAsync(async value => await observer(this));
        _diningRoomDeskSwitch.SubscribeToStateChangesAsync(async value => await observer(this));
        _diningRoomDeskPlugPower.SubscribeToStateChangesAsync(async value => await observer(this));
    }
}

public class CustomSwitchWithConditions : ICustomSwitchEntity
{
    private readonly ICustomSwitchEntity _bedroom1HeaterSwitch;
    private readonly Func<bool> _condition;

    public string EntityId => _bedroom1HeaterSwitch.EntityId;

    public CustomSwitchWithConditions(ICustomSwitchEntity customSwitch, Func<bool> condition)
    {
        _bedroom1HeaterSwitch = customSwitch;
        _condition = condition;
    }

    public bool IsOff() => _bedroom1HeaterSwitch.IsOff();

    public bool IsOn() => _bedroom1HeaterSwitch.IsOn();

    public void SubscribeToStateChangesAsync(Func<ICustomSwitchEntity, Task> observer)
    {
        _bedroom1HeaterSwitch.SubscribeToStateChangesAsync(async value => await observer(this));
    }

    public void TurnOff() => _bedroom1HeaterSwitch.TurnOff();

    public void TurnOn()
    {
        if (!_condition())
        {
            // Condition not met
            return;
        }

        _bedroom1HeaterSwitch.TurnOn();
    }
}

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
        lightEntity.CallService("turn_on", new { rgb_color = new[] { r, g, b } });
        return lightEntity;
    }

    public static LightEntity SetRgb(this LightEntity lightEntity, int r, int g, int b)
    {
        lightEntity.CallService("turn_on", new { rgb_color = new[] { r, g, b } });
        return lightEntity;
    }

    public static LightEntity SetRgb(this LightEntity lightEntity, int r, int g, int b, long brightnessPercent)
    {
        lightEntity.CallService("turn_on", new { rgb_color = new[] { r, g, b }, brightness_pct = brightnessPercent });
        return lightEntity;
    }

    public static LightEntity SetBrightnessPercent(this LightEntity lightEntity, long? brightnessPercent)
    {
        lightEntity.CallService("turn_on", new { brightness_pct = brightnessPercent });
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
    private readonly IHaContext _ha;

    public BinarySensorEntity IgnitionEntity => new(_ha, "binary_sensor.mg_mg4_electric_engine_status");
    public SwitchEntity FrontDefrostEntity => new(_ha, "switch.mg_mg4_electric_front_defrost");
    public SwitchEntity RearDefrostEntity => new(_ha, "switch.mg_mg4_electric_rear_window_defrost");

    public bool? IgnitionState => IgnitionEntity.IsOn;

    public Mg4Ev(IHaContext ha)
    {
        _ha = ha;
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

public interface ICustomBooleanSensorEntity : ICustomEntity<ICustomBooleanSensorEntity>
{
    bool? State { get; }
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
        _climateEntity.CallService("set_temperature", new { temperature });
    }
}

internal static class EntityExistenceValidator
{
    internal static void ValidateEntities(IHaContext ha, ILogger logger, params string[] entityIds)
    {
        foreach (string entityId in entityIds)
        {
            if (ha.GetState(entityId) == null)
            {
                logger.LogWarning("Entity '{EntityId}' not found in Home Assistant. Features depending on this entity will not work.", entityId);
            }
        }
    }
}