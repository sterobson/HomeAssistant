using HomeAssistant.Shared.Climate;
using HomeAssistant.Functions;
using Shouldly;
using System.Text.Json;

namespace HomeAssistant.Tests;

[TestClass]
public class JsonSerializationTests
{
    private static JsonSerializerOptions GetJsonOptions()
    {
        return JsonConfiguration.CreateOptions();
    }

    [TestMethod]
    public void Deserialize_RoomSchedulesDto_FromJson_WithEnumsAsStrings()
    {
        // Arrange - JSON as it would be stored in blob storage
        var json = @"{
            ""rooms"": [
                {
                    ""id"": 1,
                    ""name"": ""Kitchen"",
                    ""boost"": null,
                    ""schedules"": [
                        {
                            ""id"": 101,
                            ""time"": ""07:30"",
                            ""temperature"": 21.5,
                            ""rampUpMinutes"": 30,
                            ""days"": ""Weekdays"",
                            ""conditions"": ""RoomInUse""
                        }
                    ]
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        result.ShouldNotBeNull();
        result.Rooms.ShouldNotBeNull();
        result.Rooms.Count.ShouldBe(1);

        var room = result.Rooms[0];
        room.Id.ShouldBe(1);
        room.Name.ShouldBe("Kitchen");
        room.Boost.ShouldBeNull();
        room.Schedules.Count.ShouldBe(1);

        var schedule = room.Schedules[0];
        schedule.Id.ShouldBe(101);
        schedule.Time.ShouldBe("07:30");
        schedule.Temperature.ShouldBe(21.5);
        schedule.RampUpMinutes.ShouldBe(30);
        schedule.Days.ShouldBe(Days.Weekdays);
        schedule.Conditions.ShouldBe(ConditionType.RoomInUse);
    }

    [TestMethod]
    public void Deserialize_RoomSchedulesDto_FromJson_WithEnumsAsNumbers()
    {
        // Arrange - JSON with enum values as numbers
        var json = @"{
            ""rooms"": [
                {
                    ""id"": 2,
                    ""name"": ""Bedroom"",
                    ""boost"": {
                        ""startTime"": ""2025-01-15T10:00:00Z"",
                        ""endTime"": ""2025-01-15T12:00:00Z"",
                        ""temperature"": 22.0
                    },
                    ""schedules"": [
                        {
                            ""id"": 201,
                            ""time"": ""06:00"",
                            ""temperature"": 19.0,
                            ""rampUpMinutes"": 45,
                            ""days"": 31,
                            ""conditions"": 4
                        }
                    ]
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        result.ShouldNotBeNull();
        result.Rooms.Count.ShouldBe(1);

        var room = result.Rooms[0];
        room.Id.ShouldBe(2);
        room.Name.ShouldBe("Bedroom");
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldBe("2025-01-15T10:00:00Z");
        room.Boost.EndTime.ShouldBe("2025-01-15T12:00:00Z");
        room.Boost.Temperature.ShouldBe(22.0);

        var schedule = room.Schedules[0];
        schedule.Id.ShouldBe(201);
        schedule.Time.ShouldBe("06:00");
        schedule.Temperature.ShouldBe(19.0);
        schedule.RampUpMinutes.ShouldBe(45);
        schedule.Days.ShouldBe(Days.Weekdays); // 31 = Weekdays
        schedule.Conditions.ShouldBe(ConditionType.RoomInUse); // 4 = RoomInUse
    }

    [TestMethod]
    public void Deserialize_RoomSchedulesDto_WithMultipleDaysFlags()
    {
        // Arrange - Testing bit flag combinations
        var json = @"{
            ""rooms"": [
                {
                    ""id"": 3,
                    ""name"": ""Living Room"",
                    ""schedules"": [
                        {
                            ""id"": 301,
                            ""time"": ""08:00"",
                            ""temperature"": 20.0,
                            ""rampUpMinutes"": 30,
                            ""days"": 127,
                            ""conditions"": 0
                        }
                    ]
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        var schedule = result.Rooms[0].Schedules[0];
        schedule.Days.ShouldBe(Days.Everyday); // 127 = All 7 days
        schedule.Conditions.ShouldBe(ConditionType.None);
    }

    [TestMethod]
    public void Deserialize_RoomSchedulesDto_WithMultipleConditionsFlags()
    {
        // Arrange - Testing multiple conditions (bit flags)
        var json = @"{
            ""rooms"": [
                {
                    ""id"": 4,
                    ""name"": ""Office"",
                    ""schedules"": [
                        {
                            ""id"": 401,
                            ""time"": ""09:00"",
                            ""temperature"": 21.0,
                            ""rampUpMinutes"": 30,
                            ""days"": 31,
                            ""conditions"": 5
                        }
                    ]
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        var schedule = result.Rooms[0].Schedules[0];
        // 5 = Schedule1 (1) | RoomInUse (4)
        schedule.Conditions.ShouldBe(ConditionType.Schedule1 | ConditionType.RoomInUse);
    }

    [TestMethod]
    public void Deserialize_RoomStatesResponse_FromJson()
    {
        // Arrange
        var json = @"{
            ""roomStates"": [
                {
                    ""roomId"": 1,
                    ""currentTemperature"": 19.5,
                    ""heatingActive"": true,
                    ""activeScheduleTrackId"": 102,
                    ""lastUpdated"": ""2025-01-15T10:30:00Z""
                },
                {
                    ""roomId"": 2,
                    ""currentTemperature"": null,
                    ""heatingActive"": false,
                    ""activeScheduleTrackId"": 0,
                    ""lastUpdated"": ""2025-01-15T10:30:00Z""
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomStatesResponse>(json, GetJsonOptions());

        // Assert
        result.ShouldNotBeNull();
        result.RoomStates.Count.ShouldBe(2);

        var state1 = result.RoomStates[0];
        state1.RoomId.ShouldBe(1);
        state1.CurrentTemperature.ShouldBe(19.5);
        state1.HeatingActive.ShouldBeTrue();
        state1.ActiveScheduleTrackId.ShouldBe(102);
        state1.LastUpdated.ShouldBe("2025-01-15T10:30:00Z");

        var state2 = result.RoomStates[1];
        state2.RoomId.ShouldBe(2);
        state2.CurrentTemperature.ShouldBeNull();
        state2.HeatingActive.ShouldBeFalse();
        state2.ActiveScheduleTrackId.ShouldBe(0);
        state2.LastUpdated.ShouldBe("2025-01-15T10:30:00Z");
    }

    [TestMethod]
    public void Serialize_RoomSchedulesDto_ToJson_EnumsAsStrings()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms = new List<RoomDto>
            {
                new RoomDto
                {
                    Id = 1,
                    Name = "Test Room",
                    Boost = null,
                    Schedules = new List<ScheduleTrackDto>
                    {
                        new ScheduleTrackDto
                        {
                            Id = 101,
                            Time = "07:30",
                            Temperature = 21.5,
                            RampUpMinutes = 30,
                            Days = Days.Weekdays,
                            Conditions = ConditionType.RoomInUse
                        }
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(dto, GetJsonOptions());

        // Assert
        json.ShouldNotBeNullOrWhiteSpace();
        json.ShouldContain("\"id\":1");
        json.ShouldContain("\"name\":\"Test Room\"");
        json.ShouldContain("\"time\":\"07:30\"");
        json.ShouldContain("\"temperature\":21.5");
        // Enums should be serialized as strings
        json.ShouldContain("\"days\":\"Weekdays\"");
        json.ShouldContain("\"conditions\":\"RoomInUse\"");
    }

    [TestMethod]
    public void RoundTrip_RoomSchedulesDto_PreservesAllData()
    {
        // Arrange
        var original = new RoomSchedulesDto
        {
            Rooms = new List<RoomDto>
            {
                new RoomDto
                {
                    Id = 5,
                    Name = "Round Trip Test",
                    Boost = new BoostDto
                    {
                        StartTime = "2025-01-15T10:00:00Z",
                        EndTime = "2025-01-15T12:00:00Z",
                        Temperature = 23.0
                    },
                    Schedules = new List<ScheduleTrackDto>
                    {
                        new ScheduleTrackDto
                        {
                            Id = 501,
                            Time = "06:30",
                            Temperature = 20.5,
                            RampUpMinutes = 45,
                            Days = Days.Monday | Days.Wednesday | Days.Friday,
                            Conditions = ConditionType.Schedule1 | ConditionType.RoomInUse
                        }
                    }
                }
            }
        };

        // Act - Serialize then deserialize
        var json = JsonSerializer.Serialize(original, GetJsonOptions());
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        result.ShouldNotBeNull();
        result.Rooms.Count.ShouldBe(original.Rooms.Count);

        var resultRoom = result.Rooms[0];
        var originalRoom = original.Rooms[0];

        resultRoom.Id.ShouldBe(originalRoom.Id);
        resultRoom.Name.ShouldBe(originalRoom.Name);
        resultRoom.Boost.ShouldNotBeNull();
        resultRoom.Boost.StartTime.ShouldBe(originalRoom.Boost.StartTime);
        resultRoom.Boost.EndTime.ShouldBe(originalRoom.Boost.EndTime);
        resultRoom.Boost.Temperature.ShouldBe(originalRoom.Boost.Temperature);

        var resultSchedule = resultRoom.Schedules[0];
        var originalSchedule = originalRoom.Schedules[0];

        resultSchedule.Id.ShouldBe(originalSchedule.Id);
        resultSchedule.Time.ShouldBe(originalSchedule.Time);
        resultSchedule.Temperature.ShouldBe(originalSchedule.Temperature);
        resultSchedule.RampUpMinutes.ShouldBe(originalSchedule.RampUpMinutes);
        resultSchedule.Days.ShouldBe(originalSchedule.Days);
        resultSchedule.Conditions.ShouldBe(originalSchedule.Conditions);
    }

    [TestMethod]
    public void Deserialize_RoomSchedulesDto_WithSpecialDayCombinations()
    {
        // Arrange - Test special day combinations
        var json = @"{
            ""rooms"": [
                {
                    ""id"": 1,
                    ""name"": ""Test"",
                    ""schedules"": [
                        { ""id"": 1, ""time"": ""08:00"", ""temperature"": 20, ""days"": 31 },
                        { ""id"": 2, ""time"": ""08:00"", ""temperature"": 20, ""days"": 96 },
                        { ""id"": 3, ""time"": ""08:00"", ""temperature"": 20, ""days"": 63 },
                        { ""id"": 4, ""time"": ""08:00"", ""temperature"": 20, ""days"": 127 }
                    ]
                }
            ]
        }";

        // Act
        var result = JsonSerializer.Deserialize<RoomSchedulesDto>(json, GetJsonOptions());

        // Assert
        result.Rooms[0].Schedules[0].Days.ShouldBe(Days.Weekdays); // 31
        result.Rooms[0].Schedules[1].Days.ShouldBe(Days.Weekends); // 96
        result.Rooms[0].Schedules[2].Days.ShouldBe(Days.NotSunday); // 63
        result.Rooms[0].Schedules[3].Days.ShouldBe(Days.Everyday); // 127
    }
}
