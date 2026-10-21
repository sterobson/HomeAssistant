using HomeAssistant.apps;

namespace HomeAssistant.Tests;

internal class FakeNamedEntities : INamedEntities
{
    public ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DiningRoomDehumidifierSmartPlugPower) };

    public ICustomNumericSensorEntity DiningRoomClimateHumidity { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DiningRoomClimateHumidity) };

    public ICustomNumericSensorEntity DiningRoomDeskPlugPower { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DiningRoomDeskPlugPower) };

    public ICustomNumericSensorEntity KitchenTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(KitchenTemperature) };

    public ICustomSwitchEntity KitchenHeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(KitchenHeaterSmartPlugOnOff) };

    public ICustomNumericSensorEntity GamesRoomDeskTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(GamesRoomDeskTemperature) };

    public ICustomNumericSensorEntity GamesRoomDeskHumidity { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(GamesRoomDeskHumidity) };

    public ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(GamesRoomHeaterSmartPlugOnOff) };

    public ICustomSwitchEntity GamesRoomDeskPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(GamesRoomDeskPlugOnOff) };

    public ICustomNumericSensorEntity GamesRoomDeskPlugPower { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(GamesRoomDeskPlugPower) };

    public ICustomNumericSensorEntity Bedroom1Temperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(Bedroom1Temperature) };

    public ICustomClimateControlEntity Bedroom1RadiatorThermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(Bedroom1RadiatorThermostat) };

    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(Bedroom1HeaterSmartPlugOnOff) };

    public ICustomSwitchEntity DiningRoomDeskPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(DiningRoomDeskPlugOnOff) };

    public ICustomSwitchEntity DiningRoomHeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(DiningRoomHeaterSmartPlugOnOff) };

    public ICustomNumericSensorEntity DiningRoomClimateTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DiningRoomClimateTemperature) };

    public ICustomClimateControlEntity DiningRoomRadiatorThermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(DiningRoomRadiatorThermostat) };

    public ICustomNumericSensorEntity LivingRoomClimateHumidity { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(LivingRoomClimateHumidity) };

    public ICustomNumericSensorEntity LivingRoomClimateTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(LivingRoomClimateTemperature) };

    public ICustomClimateControlEntity LivingRoomRadiatorThermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(LivingRoomRadiatorThermostat) };

    public ICustomNumericSensorEntity Bedroom2Temperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(Bedroom2Temperature) };

    public ICustomClimateControlEntity Bedroom2RadiatorThermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(Bedroom2RadiatorThermostat) };

    public ICustomNumericSensorEntity DenClimateTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DenClimateTemperature) };

    public ICustomNumericSensorEntity HallwayClimateTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(HallwayClimateTemperature) };

    public ICustomClimateControlEntity DenRadiator1Thermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(DenRadiator1Thermostat) };

    public ICustomClimateControlEntity DenRadiator2Thermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(DenRadiator2Thermostat) };

    public ICustomNumericSensorEntity LivingRoomTvPlugPower { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(LivingRoomTvPlugPower) };

    public ICustomSwitchEntity LivingRoomStickLightsPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(LivingRoomStickLightsPlugOnOff) };

    public ICustomNumericSensorEntity DenTvPlugPower { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DenTvPlugPower) };

    public ICustomNumericSensorEntity Bedroom3Temperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(Bedroom3Temperature) };

    public ICustomClimateControlEntity Bedroom3RadiatorThermostat { get; } = new FakeCustomClimateControlEntity { EntityId = nameof(Bedroom3RadiatorThermostat) };

    public ICustomNumericSensorEntity UpstairsBathroomTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(UpstairsBathroomTemperature) };


    // Reachable and reporting "off", as the real one does between presses; a fingerbot with no
    // state at all is unavailable, and the app refuses to press it.
    public ICustomSwitchEntity BoilerFingerbot { get; } = new FakeCustomSwitchEntity { EntityId = nameof(BoilerFingerbot), InitialState = false };
}

public class FakeCustomNumericSensorEntity : ICustomNumericSensorEntity
{
    private Func<ICustomNumericSensorEntity, Task>? _stateChangeObserver;
    private double? _state;

    public double? State
    {
        get => _state;
        set
        {
            _state = value;
            _stateChangeObserver?.Invoke(this);
        }
    }

    public required string EntityId { get; init; }

    public void SubscribeToStateChangesAsync(Func<ICustomNumericSensorEntity, Task> observer)
    {
        _stateChangeObserver = observer;
    }
}

public class FakeCustomSwitchEntity : ICustomSwitchEntity
{
    private Func<ICustomSwitchEntity, Task>? _stateChangeObserver;
    private bool? _state;
    public required string EntityId { get; init; }

    /// <summary>
    /// How many times this switch has actually been driven. For the boiler fingerbot each one
    /// is a physical press of the boiler's button, so tests count them rather than reading state.
    /// </summary>
    public int TurnOnCount { get; private set; }

    public int TurnOffCount { get; private set; }

    public int ToggleCount => TurnOnCount + TurnOffCount;

    /// <summary>The state before anything happens. Null means unknown or unavailable.</summary>
    public bool? InitialState { init => _state = value; }

    /// <summary>
    /// Changes the reported state as something other than the app would: the HA UI, a script, or the
    /// device dropping off the network (null). Not counted as one of the app's commands. Returns the
    /// subscriber's task so a test can wait for whatever it set off.
    /// </summary>
    public Task SetStateFromOutsideTheApp(bool? state)
    {
        if (_state == state)
            return Task.CompletedTask;

        _state = state;
        return _stateChangeObserver?.Invoke(this) ?? Task.CompletedTask;
    }

    public bool IsOff() => _state == false;

    public bool IsOn() => _state == true;

    public void SubscribeToStateChangesAsync(Func<ICustomSwitchEntity, Task> observer)
    {
        _stateChangeObserver = observer;
    }

    /// <summary>
    /// Like a real Zigbee device, the command is sent but HA only reports the new state a moment later, so
    /// anything reading the switch straight after a command still sees the old state. Call
    /// <see cref="ReportNow"/> to deliver it.
    /// </summary>
    public bool ReportsLate { get; set; }

    private bool? _pendingState;

    public void ReportNow()
    {
        if (_pendingState.HasValue)
        {
            _state = _pendingState;
            _pendingState = null;
        }
    }

    public void TurnOff()
    {
        TurnOffCount++;
        Set(false);
    }

    public void TurnOn()
    {
        TurnOnCount++;
        Set(true);
    }

    private void Set(bool state)
    {
        if (ReportsLate)
        {
            _pendingState = state;
            return;
        }

        if (_state != state)
        {
            _state = state;
            _stateChangeObserver?.Invoke(this);
        }
    }
}

public class FakeCustomClimateControlEntity : ICustomClimateControlEntity
{
    private Func<ICustomClimateControlEntity, Task>? _stateChangeObserver;

    public double? CurrentTemperature { get; set; }

    public double? TargetTemperature { get; private set; }

    public required string EntityId { get; init; }

    /// <summary>How many setpoint commands were sent: each one is a Zigbee message the TRV wakes for.</summary>
    public int SetCount { get; set; }

    /// <summary>
    /// Like Home Assistant, which reports a command a moment after it is sent: a new setpoint shows only once
    /// <see cref="Land"/> is called, so anything reading the valve in the same pass sees the old one.
    /// </summary>
    public bool ReportsLate { get; set; }

    private double? _setpointInFlight;

    public void SetTargetTemperature(double temperature)
    {
        SetCount++;
        if (ReportsLate)
        {
            _setpointInFlight = temperature;
            return;
        }

        TargetTemperature = temperature;
    }

    /// <summary>Shows the setpoint last sent while <see cref="ReportsLate"/>.</summary>
    public void Land()
    {
        if (_setpointInFlight.HasValue)
        {
            TargetTemperature = _setpointInFlight;
            _setpointInFlight = null;
        }
    }

    public void SubscribeToStateChangesAsync(Func<ICustomClimateControlEntity, Task> observer)
    {
        _stateChangeObserver = observer;
    }
}