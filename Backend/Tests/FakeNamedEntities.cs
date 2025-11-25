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

    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(Bedroom1HeaterSmartPlugOnOff) };

    public ICustomSwitchEntity DiningRoomDeskPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(DiningRoomDeskPlugOnOff) };

    public ICustomSwitchEntity DiningRoomHeaterSmartPlugOnOff { get; } = new FakeCustomSwitchEntity { EntityId = nameof(DiningRoomHeaterSmartPlugOnOff) };

    public ICustomNumericSensorEntity DiningRoomClimateTemperature { get; } = new FakeCustomNumericSensorEntity { EntityId = nameof(DiningRoomClimateTemperature) };
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

    public bool IsOff() => _state == false;

    public bool IsOn() => _state == true;

    public void SubscribeToStateChangesAsync(Func<ICustomSwitchEntity, Task> observer)
    {
        _stateChangeObserver = observer;
    }

    public void TurnOff()
    {
        if (_state != false)
        {
            _state = false;
            _stateChangeObserver?.Invoke(this);
        }
    }

    public void TurnOn()
    {
        if (_state != true)
        {
            _state = true;
            _stateChangeObserver?.Invoke(this);
        }
    }
}