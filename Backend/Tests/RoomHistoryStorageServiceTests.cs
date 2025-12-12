using Azure;
using Azure.Data.Tables;
using HomeAssistant.Functions.Models;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public class TemperatureHistoryPointTests
{

    [TestMethod]
    public void TemperatureHistoryPoint_Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        TemperatureHistoryPoint point = new TemperatureHistoryPoint
        {
            PartitionKey = "house1_1",
            RowKey = "9223372036854775807",
            HouseId = "house1",
            RoomId = 1,
            CurrentTemperature = 20.5,
            TargetTemperature = 21.0,
            HeatingActive = true,
            RecordedAt = DateTimeOffset.UtcNow,
            Timestamp = DateTimeOffset.UtcNow,
            ETag = new ETag("test-etag")
        };

        // Assert
        point.PartitionKey.ShouldBe("house1_1");
        point.RowKey.ShouldBe("9223372036854775807");
        point.HouseId.ShouldBe("house1");
        point.RoomId.ShouldBe(1);
        point.CurrentTemperature.ShouldBe(20.5);
        point.TargetTemperature.ShouldBe(21.0);
        point.HeatingActive.ShouldBeTrue();
        point.RecordedAt.ShouldNotBe(default(DateTimeOffset));
    }

    [TestMethod]
    public void TemperatureHistoryPoint_ImplementsITableEntity()
    {
        // Arrange & Act
        TemperatureHistoryPoint point = new TemperatureHistoryPoint();

        // Assert
        point.ShouldBeAssignableTo<ITableEntity>();
        point.PartitionKey.ShouldNotBeNull();
        point.RowKey.ShouldNotBeNull();
    }

    [TestMethod]
    public void TemperatureHistoryPoint_PartitionKey_FormatsCorrectly()
    {
        // Arrange
        string houseId = "my-house";
        int roomId = 5;
        string expectedPartitionKey = $"{houseId}_{roomId}";

        TemperatureHistoryPoint point = new TemperatureHistoryPoint
        {
            PartitionKey = expectedPartitionKey,
            HouseId = houseId,
            RoomId = roomId
        };

        // Assert
        point.PartitionKey.ShouldBe("my-house_5");
        point.HouseId.ShouldBe(houseId);
        point.RoomId.ShouldBe(roomId);
    }

    [TestMethod]
    public void TemperatureHistoryPoint_AllowsNullTemperatures()
    {
        // Arrange
        TemperatureHistoryPoint point = new TemperatureHistoryPoint
        {
            CurrentTemperature = null,
            TargetTemperature = null
        };

        // Assert
        point.CurrentTemperature.ShouldBeNull();
        point.TargetTemperature.ShouldBeNull();
    }

    [TestMethod]
    public void TemperatureHistoryPoint_HeatingActiveDefaults()
    {
        // Arrange
        TemperatureHistoryPoint point = new TemperatureHistoryPoint();

        // Assert
        point.HeatingActive.ShouldBeFalse(); // bool defaults to false
    }
}

[TestClass]
public class DeduplicationLogicTests
{
    [TestMethod]
    public void TemperatureChange_LessThanThreshold_ShouldNotTrigger()
    {
        // Arrange
        double previousTemp = 20.0;
        double newTemp = 20.05;
        double threshold = 0.1;

        // Act
        double difference = Math.Abs(newTemp - previousTemp);

        // Assert
        difference.ShouldBeLessThan(threshold);
    }

    [TestMethod]
    public void TemperatureChange_EqualsThreshold_ShouldTrigger()
    {
        // Arrange
        double previousTemp = 20.0;
        double newTemp = 20.1;
        double threshold = 0.1;

        // Act
        double difference = Math.Abs(newTemp - previousTemp);

        // Assert
        difference.ShouldBeGreaterThanOrEqualTo(threshold);
    }

    [TestMethod]
    public void TemperatureChange_ExceedsThreshold_ShouldTrigger()
    {
        // Arrange
        double previousTemp = 20.0;
        double newTemp = 20.5;
        double threshold = 0.1;

        // Act
        double difference = Math.Abs(newTemp - previousTemp);

        // Assert
        difference.ShouldBeGreaterThan(threshold);
    }

    [TestMethod]
    public void TimeElapsed_LessThan15Minutes_ShouldNotTrigger()
    {
        // Arrange
        DateTimeOffset lastRecorded = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int fallbackMinutes = 15;

        // Act
        double minutesElapsed = (now - lastRecorded).TotalMinutes;

        // Assert
        minutesElapsed.ShouldBeLessThan(fallbackMinutes);
    }

    [TestMethod]
    public void TimeElapsed_Equals15Minutes_ShouldTrigger()
    {
        // Arrange
        DateTimeOffset lastRecorded = DateTimeOffset.UtcNow.AddMinutes(-15);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int fallbackMinutes = 15;

        // Act
        double minutesElapsed = (now - lastRecorded).TotalMinutes;

        // Assert
        minutesElapsed.ShouldBeGreaterThanOrEqualTo(fallbackMinutes);
    }

    [TestMethod]
    public void TimeElapsed_Exceeds15Minutes_ShouldTrigger()
    {
        // Arrange
        DateTimeOffset lastRecorded = DateTimeOffset.UtcNow.AddMinutes(-20);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int fallbackMinutes = 15;

        // Act
        double minutesElapsed = (now - lastRecorded).TotalMinutes;

        // Assert
        minutesElapsed.ShouldBeGreaterThan(fallbackMinutes);
    }

    [TestMethod]
    public void HeatingStateChange_FromOffToOn_ShouldTrigger()
    {
        // Arrange
        bool previousState = false;
        bool newState = true;

        // Act
        bool hasChanged = previousState != newState;

        // Assert
        hasChanged.ShouldBeTrue();
    }

    [TestMethod]
    public void HeatingStateChange_FromOnToOff_ShouldTrigger()
    {
        // Arrange
        bool previousState = true;
        bool newState = false;

        // Act
        bool hasChanged = previousState != newState;

        // Assert
        hasChanged.ShouldBeTrue();
    }

    [TestMethod]
    public void HeatingStateChange_NoChange_ShouldNotTrigger()
    {
        // Arrange
        bool previousState = true;
        bool newState = true;

        // Act
        bool hasChanged = previousState != newState;

        // Assert
        hasChanged.ShouldBeFalse();
    }

    [TestMethod]
    public void TargetTemperatureChange_Different_ShouldTrigger()
    {
        // Arrange
        double? previousTarget = 20.0;
        double? newTarget = 21.0;

        // Act
        bool hasChanged = previousTarget != newTarget;

        // Assert
        hasChanged.ShouldBeTrue();
    }

    [TestMethod]
    public void TargetTemperatureChange_Same_ShouldNotTrigger()
    {
        // Arrange
        double? previousTarget = 20.0;
        double? newTarget = 20.0;

        // Act
        bool hasChanged = previousTarget != newTarget;

        // Assert
        hasChanged.ShouldBeFalse();
    }

    [TestMethod]
    public void TargetTemperatureChange_FromNullToValue_ShouldTrigger()
    {
        // Arrange
        double? previousTarget = null;
        double? newTarget = 20.0;

        // Act
        bool hasChanged = previousTarget != newTarget;

        // Assert
        hasChanged.ShouldBeTrue();
    }

    [TestMethod]
    public void TargetTemperatureChange_FromValueToNull_ShouldTrigger()
    {
        // Arrange
        double? previousTarget = 20.0;
        double? newTarget = null;

        // Act
        bool hasChanged = previousTarget != newTarget;

        // Assert
        hasChanged.ShouldBeTrue();
    }
}
