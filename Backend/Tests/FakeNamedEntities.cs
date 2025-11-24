using HomeAssistant.apps;

namespace HomeAssistant.Tests;

internal class FakeNamedEntities : INamedEntities
{
    public ICustomNumericSensorEntity DiningRoomDehumidifierSmartPlugPower => throw new NotImplementedException();

    public ICustomNumericSensorEntity DiningRoomClimateHumidity => throw new NotImplementedException();

    public ICustomNumericSensorEntity DiningRoomDeskPlugPower => throw new NotImplementedException();

    public ICustomNumericSensorEntity KitchenTemperature => throw new NotImplementedException();

    public ICustomSwitchEntity KitchenHeaterSmartPlugOnOff => throw new NotImplementedException();

    public ICustomNumericSensorEntity GamesRoomDeskTemperature => throw new NotImplementedException();

    public ICustomNumericSensorEntity GamesRoomDeskHumidity => throw new NotImplementedException();

    public ICustomSwitchEntity GamesRoomHeaterSmartPlugOnOff => throw new NotImplementedException();

    public ICustomSwitchEntity GamesRoomDeskPlugOnOff => throw new NotImplementedException();

    public ICustomNumericSensorEntity GamesRoomDeskPlugPower => throw new NotImplementedException();

    public ICustomNumericSensorEntity Bedroom1Temperature => throw new NotImplementedException();

    public ICustomSwitchEntity Bedroom1HeaterSmartPlugOnOff => throw new NotImplementedException();
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