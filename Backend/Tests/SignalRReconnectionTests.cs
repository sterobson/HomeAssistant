using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Services.Energy;
using HomeAssistant.Shared;
using HomeAssistant.Shared.Energy;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class SignalRReconnectionTests
{
    private const string TestHouseId = "test-house-123";

    #region FakeSignalRConnectionService

    private sealed class FakeSignalRConnectionService : ISignalRConnectionService
    {
        private readonly Dictionary<string, List<Delegate>> _handlers = new();

        public event Func<Task>? ConnectionRestored;

        public int RegisteredHandlerCount => _handlers.Values.Sum(list => list.Count);

        public Task StartAsync() => Task.CompletedTask;

        public void On<T>(string methodName, Func<T, Task> handler)
        {
            if (!_handlers.TryGetValue(methodName, out List<Delegate>? handlers))
            {
                handlers = new List<Delegate>();
                _handlers[methodName] = handlers;
            }

            handlers.Add(handler);
        }

        public async Task SimulateConnectionRestored()
        {
            if (ConnectionRestored != null)
            {
                await ConnectionRestored.Invoke();
            }
        }

        public async Task SimulateMessage<T>(string methodName, T payload)
        {
            if (_handlers.TryGetValue(methodName, out List<Delegate>? handlers))
            {
                foreach (Delegate handler in handlers)
                {
                    if (handler is Func<T, Task> typedHandler)
                    {
                        await typedHandler(payload);
                    }
                }
            }
        }
    }

    #endregion

    #region Battery rules

    [TestMethod]
    public async Task BatteryRules_RefreshesFromApi_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IBatteryRulesApiClient apiClient = Substitute.For<IBatteryRulesApiClient>();
        apiClient.GetRulesAsync(TestHouseId).Returns(new BatteryZoneRules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        BatteryRulesPersistenceService service = new(
            Substitute.For<ILogger<BatteryRulesPersistenceService>>(),
            config,
            Substitute.For<IGracefulShutdownService>(),
            apiClient,
            fakeSignalR);

        await service.StartAsync();
        apiClient.ClearReceivedCalls();

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        await apiClient.Received(1).GetRulesAsync(TestHouseId);
    }

    [TestMethod]
    public async Task BatteryRules_FiresRulesUpdatedEvent_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IBatteryRulesApiClient apiClient = Substitute.For<IBatteryRulesApiClient>();
        apiClient.GetRulesAsync(TestHouseId).Returns(new BatteryZoneRules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        BatteryRulesPersistenceService service = new(
            Substitute.For<ILogger<BatteryRulesPersistenceService>>(),
            config,
            Substitute.For<IGracefulShutdownService>(),
            apiClient,
            fakeSignalR);

        await service.StartAsync();

        bool rulesUpdatedFired = false;
        service.RulesUpdated += () =>
        {
            rulesUpdatedFired = true;
            return Task.CompletedTask;
        };

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        rulesUpdatedFired.ShouldBeTrue();
    }

    [TestMethod]
    public async Task BatteryRules_RefreshesFromApi_WhenSignalRMessageReceived()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IBatteryRulesApiClient apiClient = Substitute.For<IBatteryRulesApiClient>();
        apiClient.GetRulesAsync(TestHouseId).Returns(new BatteryZoneRules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        BatteryRulesPersistenceService service = new(
            Substitute.For<ILogger<BatteryRulesPersistenceService>>(),
            config,
            Substitute.For<IGracefulShutdownService>(),
            apiClient,
            fakeSignalR);

        await service.StartAsync();
        apiClient.ClearReceivedCalls();

        // Act
        await fakeSignalR.SimulateMessage<object>("battery-rules-changed", new object());

        // Assert
        await apiClient.Received(1).GetRulesAsync(TestHouseId);
    }

    #endregion

    #region Schedules

    [TestMethod]
    public async Task Schedules_RefreshesFromApi_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IScheduleApiClient scheduleApiClient = Substitute.For<IScheduleApiClient>();
        scheduleApiClient.GetSchedulesAsync(TestHouseId).Returns(new RoomSchedules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        SchedulePersistenceService service = new(
            Substitute.For<ILogger<SchedulePersistenceService>>(),
            config,
            scheduleApiClient,
            fakeSignalR);

        await service.StartAsync();

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        await scheduleApiClient.Received(1).GetSchedulesAsync(TestHouseId);
    }

    [TestMethod]
    public async Task Schedules_FiresSchedulesUpdatedEvent_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IScheduleApiClient scheduleApiClient = Substitute.For<IScheduleApiClient>();
        scheduleApiClient.GetSchedulesAsync(TestHouseId).Returns(new RoomSchedules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        SchedulePersistenceService service = new(
            Substitute.For<ILogger<SchedulePersistenceService>>(),
            config,
            scheduleApiClient,
            fakeSignalR);

        await service.StartAsync();

        bool schedulesUpdatedFired = false;
        service.SchedulesUpdated += () =>
        {
            schedulesUpdatedFired = true;
            return Task.CompletedTask;
        };

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        schedulesUpdatedFired.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Schedules_RefreshesFromApi_WhenSignalRMessageReceived()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IScheduleApiClient scheduleApiClient = Substitute.For<IScheduleApiClient>();
        scheduleApiClient.GetSchedulesAsync(TestHouseId).Returns(new RoomSchedules());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        SchedulePersistenceService service = new(
            Substitute.For<ILogger<SchedulePersistenceService>>(),
            config,
            scheduleApiClient,
            fakeSignalR);

        await service.StartAsync();

        // Act
        await fakeSignalR.SimulateMessage<object>("schedules-changed", new object());

        // Assert
        await scheduleApiClient.Received(1).GetSchedulesAsync(TestHouseId);
    }

    #endregion

    #region Device settings

    [TestMethod]
    public async Task DeviceSettings_RefreshesFromApi_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IDeviceSettingsApiClient apiClient = Substitute.For<IDeviceSettingsApiClient>();
        apiClient.GetSettingsAsync(TestHouseId).Returns(new DeviceSettingsDto());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        DeviceSettingsPersistenceService service = new(
            Substitute.For<ILogger<DeviceSettingsPersistenceService>>(),
            config,
            apiClient,
            fakeSignalR);

        await service.StartAsync();

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        await apiClient.Received(1).GetSettingsAsync(TestHouseId);
    }

    [TestMethod]
    public async Task DeviceSettings_FiresSettingsUpdatedEvent_WhenConnectionRestored()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IDeviceSettingsApiClient apiClient = Substitute.For<IDeviceSettingsApiClient>();
        apiClient.GetSettingsAsync(TestHouseId).Returns(new DeviceSettingsDto());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        DeviceSettingsPersistenceService service = new(
            Substitute.For<ILogger<DeviceSettingsPersistenceService>>(),
            config,
            apiClient,
            fakeSignalR);

        await service.StartAsync();

        bool settingsUpdatedFired = false;
        service.SettingsUpdated += () =>
        {
            settingsUpdatedFired = true;
            return Task.CompletedTask;
        };

        // Act
        await fakeSignalR.SimulateConnectionRestored();

        // Assert
        settingsUpdatedFired.ShouldBeTrue();
    }

    [TestMethod]
    public async Task DeviceSettings_RefreshesFromApi_WhenSignalRMessageReceived()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        IDeviceSettingsApiClient apiClient = Substitute.For<IDeviceSettingsApiClient>();
        apiClient.GetSettingsAsync(TestHouseId).Returns(new DeviceSettingsDto());

        WebSynchronisationConfiguration config = new() { HouseId = TestHouseId, ScheduleApiUrl = "https://test" };
        DeviceSettingsPersistenceService service = new(
            Substitute.For<ILogger<DeviceSettingsPersistenceService>>(),
            config,
            apiClient,
            fakeSignalR);

        await service.StartAsync();

        // Act
        await fakeSignalR.SimulateMessage<object>("device-settings-changed", new object());

        // Assert
        await apiClient.Received(1).GetSettingsAsync(TestHouseId);
    }

    #endregion

    #region FakeSignalRConnectionService behaviour

    [TestMethod]
    public void HandlersRegisteredBeforeStart_ArePreserved()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();

        // Act — register handlers before StartAsync
        fakeSignalR.On<object>("method-1", _ => Task.CompletedTask);
        fakeSignalR.On<object>("method-2", _ => Task.CompletedTask);

        // Assert
        fakeSignalR.RegisteredHandlerCount.ShouldBe(2);

        // Act — register more after StartAsync
        fakeSignalR.StartAsync().GetAwaiter().GetResult();
        fakeSignalR.On<object>("method-3", _ => Task.CompletedTask);

        // Assert
        fakeSignalR.RegisteredHandlerCount.ShouldBe(3);
    }

    [TestMethod]
    public async Task SimulateMessage_InvokesRegisteredHandler()
    {
        // Arrange
        FakeSignalRConnectionService fakeSignalR = new();
        string? receivedPayload = null;

        fakeSignalR.On<string>("test-method", payload =>
        {
            receivedPayload = payload;
            return Task.CompletedTask;
        });

        // Act
        await fakeSignalR.SimulateMessage("test-method", "hello-world");

        // Assert
        receivedPayload.ShouldBe("hello-world");
    }

    #endregion
}
