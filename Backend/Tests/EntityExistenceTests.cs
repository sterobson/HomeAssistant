using HomeAssistant.apps;
using HomeAssistant.apps.HassModel.Climate;
using HomeAssistant.apps.HassModel.Lights;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Shared;
using HomeAssistant.Shared.Climate;
using HassModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class EntityExistenceTests
{
    /// <summary>
    /// Creates a mock IHaContext where GetState returns null for all entities (simulating non-existent entities),
    /// but StateAllChanges and Events return empty observables so subscriptions don't throw.
    /// </summary>
    private static IHaContext CreateMockHaContext()
    {
        IHaContext ha = Substitute.For<IHaContext>();
        ha.GetState(Arg.Any<string>()).Returns((EntityState?)null);
        ha.StateAllChanges().Returns(Observable.Empty<StateChange>());
        ha.Events.Returns(Observable.Empty<Event>());
        return ha;
    }

    /// <summary>
    /// Creates a logger substitute that can be verified for warning calls.
    /// </summary>
    private static ILogger<T> CreateLogger<T>()
    {
        return Substitute.For<ILogger<T>>();
    }

    [TestMethod]
    public void EntityExistenceValidator_LogsWarning_ForEachMissingEntity()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        ILogger logger = Substitute.For<ILogger>();

        string[] entityIds = ["sensor.test_entity_1", "switch.test_entity_2", "climate.test_entity_3"];

        // Act
        EntityExistenceValidator.ValidateEntities(ha, logger, entityIds);

        // Assert - GetState was called for each entity
        ha.Received(3).GetState(Arg.Any<string>());
        ha.Received(1).GetState("sensor.test_entity_1");
        ha.Received(1).GetState("switch.test_entity_2");
        ha.Received(1).GetState("climate.test_entity_3");

        // Assert - LogWarning was called for each missing entity
        logger.Received(3).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void EntityExistenceValidator_DoesNotLogWarning_ForExistingEntities()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        ILogger logger = Substitute.For<ILogger>();

        EntityState existingState = new() { EntityId = "sensor.existing" };
        ha.GetState("sensor.existing").Returns(existingState);

        // Act
        EntityExistenceValidator.ValidateEntities(ha, logger, "sensor.existing");

        // Assert - No warnings logged
        logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void GamesRoomDesk_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        NamedEntities namedEntities = new(ha);
        ILogger<GamesRoomDesk> logger = CreateLogger<GamesRoomDesk>();

        // Act & Assert - constructor should not throw
        GamesRoomDesk sut = new(ha, namedEntities, logger);

        // Verify warnings were logged for the 2 validated entities
        logger.Received(2).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void Wooooooo_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        NamedEntities namedEntities = new(ha);
        ILogger<Wooooooo> logger = CreateLogger<Wooooooo>();

        // Act & Assert - constructor should not throw
        Wooooooo sut = new(ha, namedEntities, logger);

        // Verify warnings were logged for the 3 validated entities
        logger.Received(3).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void DiningRoomLight_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        NamedEntities namedEntities = new(ha);
        IScheduler scheduler = Substitute.For<IScheduler>();
        ILogger<DiningRoomLight> logger = CreateLogger<DiningRoomLight>();

        // Act & Assert - constructor should not throw
        DiningRoomLight sut = new(ha, namedEntities, scheduler, logger);

        // Verify warnings were logged for the 7 validated entities
        logger.Received(7).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void DiningRoomDehumidifier_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        NamedEntities namedEntities = new(ha);
        HistoryService historyService = new MockHistoryService(new HomeAssistantConfiguration(), Substitute.For<ILogger<HistoryService>>());
        IScheduler scheduler = Substitute.For<IScheduler>();
        NotificationService notificationService = new(ha, new NotificationConfiguration(), CreateLogger<NotificationService>());
        ILogger<DiningRoomDehumidifier> logger = CreateLogger<DiningRoomDehumidifier>();

        // Act & Assert - constructor should not throw
        DiningRoomDehumidifier sut = new(ha, namedEntities, historyService, scheduler, notificationService, logger);

        // Verify warnings were logged for the 6 validated entities
        logger.Received(6).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void PresenceService_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        NamedEntities namedEntities = new(ha);
        HistoryService historyService = new MockHistoryService(new HomeAssistantConfiguration(), Substitute.For<ILogger<HistoryService>>());
        ILogger<PresenceService> logger = CreateLogger<PresenceService>();

        // Act & Assert - constructor should not throw
        PresenceService sut = new(ha, namedEntities, historyService, logger);

        // Verify warnings were logged for the 4 validated entities
        logger.Received(4).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public void HeatingControlService_Constructor_DoesNotThrow_WhenEntitiesDoNotExist()
    {
        // Arrange
        IHaContext ha = CreateMockHaContext();
        FakeNamedEntities namedEntities = new();
        IScheduler scheduler = Substitute.For<IScheduler>();
        ILogger<HeatingControlService> logger = CreateLogger<HeatingControlService>();
        IHomeBattery homeBattery = Substitute.For<IHomeBattery>();
        IElectricityMeter electricityMeter = Substitute.For<IElectricityMeter>();
        IPresenceService presenceService = Substitute.For<IPresenceService>();
        FakeTimeProvider timeProvider = new();
        ISchedulePersistenceService schedulePersistence = Substitute.For<ISchedulePersistenceService>();
        IRoomStatePersistenceService statePersistence = Substitute.For<IRoomStatePersistenceService>();

        // Act & Assert - constructor should not throw
        HeatingControlService sut = new(ha, namedEntities, scheduler, logger, homeBattery, electricityMeter, presenceService, timeProvider, schedulePersistence, statePersistence);

        // Verify warnings were logged for the 21 validated entities
        logger.Received(21).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

}
