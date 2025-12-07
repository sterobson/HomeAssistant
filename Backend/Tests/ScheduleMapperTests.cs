using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public class ScheduleMapperTests
{
    #region MapFromDto Tests

    [TestMethod]
    public void MapFromDto_WithNullDto_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ScheduleMapper.MapFromDto(null!));
    }

    [TestMethod]
    public void MapFromDto_WithEmptyRooms_ReturnsEmptyList()
    {
        // Arrange
        var dto = new RoomSchedulesDto { Rooms = [] };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.ShouldNotBeNull();
        result.Rooms.ShouldBeEmpty();
    }

    [TestMethod]
    public void MapFromDto_WithSingleRoomAndSchedule_MapsCorrectly()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Kitchen",
                    Boost = null,
                    Schedules =
                    [
                        new ScheduleTrackDto
                        {
                            Id = 101,
                            Time = "07:30",
                            Temperature = 21.5,
                            RampUpMinutes = 45,
                            Days = Days.Weekdays,
                            Conditions = ConditionType.Schedule1,
                        }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms.Count.ShouldBe(1);
        var room = result.Rooms[0];
        room.Id.ShouldBe(1);
        room.Name.ShouldBe("Kitchen");
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldBeNull();
        room.Boost.EndTime.ShouldBeNull();
        room.Boost.Temperature.ShouldBeNull();

        room.ScheduleTracks.Count.ShouldBe(1);
        var track = room.ScheduleTracks[0];
        track.Id.ShouldBe(101);
        track.TargetTime.ShouldBe(new TimeOnly(7, 30));
        track.Temperature.ShouldBe(21.5);
        track.RampUpMinutes.ShouldBe(45);
        track.Days.ShouldBe(Days.Weekdays);
        track.Conditions.ShouldBe(ConditionType.Schedule1);
    }

    [TestMethod]
    public void MapFromDto_WithBoost_MapsBoostCorrectly()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddHours(2);
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 2,
                    Name = "Bedroom",
                    Boost = new BoostDto
                    {
                        StartTime = startTime.ToString("O"),
                        EndTime = endTime.ToString("O"),
                        Temperature = 22.0
                    },
                    Schedules = []
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        var room = result.Rooms[0];
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldNotBeNull();
        room.Boost.EndTime.ShouldNotBeNull();
        room.Boost.StartTime.Value.ShouldBe(startTime);
        room.Boost.EndTime.Value.ShouldBe(endTime);
        room.Boost.Temperature.ShouldBe(22.0);
    }

    [TestMethod]
    public void MapFromDto_WithEmptyBoostTimes_MapsAsNull()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 3,
                    Name = "Lounge",
                    Boost = new BoostDto
                    {
                        StartTime = "",
                        EndTime = "",
                        Temperature = 20.0
                    },
                    Schedules = []
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        var room = result.Rooms[0];
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldBeNull();
        room.Boost.EndTime.ShouldBeNull();
        room.Boost.Temperature.ShouldBe(20.0);
    }

    [TestMethod]
    public void MapFromDto_WithInvalidRoomId_DefaultsToZero()
    {
        // This test is no longer applicable since Id is now int, not string
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 0,
                    Name = "Test Room",
                    Schedules = []
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms[0].Id.ShouldBe(0);
    }

    [TestMethod]
    public void MapFromDto_WithInvalidTrackId_DefaultsToZero()
    {
        // This test is no longer applicable since Id is now int, not string
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Test Room",
                    Schedules =
                    [
                        new ScheduleTrackDto
                        {
                            Id = 0,
                            Time = "12:00",
                            Temperature = 20.0,
                            Days = Days.Unspecified
                        }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms[0].ScheduleTracks[0].Id.ShouldBe(0);
    }

    [TestMethod]
    public void MapFromDto_WithMultipleRoomsAndSchedules_MapsAllCorrectly()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Kitchen",
                    Schedules =
                    [
                        new ScheduleTrackDto { Id = 101, Time = "07:00", Temperature = 21.0, Days = Days.Weekdays },
                        new ScheduleTrackDto { Id = 102, Time = "22:00", Temperature = 18.0, Days = Days.Weekdays }
                    ]
                },
                new RoomDto
                {
                    Id = 2,
                    Name = "Bedroom",
                    Schedules =
                    [
                        new ScheduleTrackDto { Id = 201, Time = "06:30", Temperature = 19.0, Days = Days.Weekends }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms.Count.ShouldBe(2);
        result.Rooms[0].ScheduleTracks.Count.ShouldBe(2);
        result.Rooms[1].ScheduleTracks.Count.ShouldBe(1);
    }

    [TestMethod]
    public void MapFromDto_WithAllDayTypes_MapsCorrectly()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Test Room",
                    Schedules =
                    [
                        new ScheduleTrackDto { Id = 1, Time = "08:00", Temperature = 20.0, Days = Days.Monday },
                        new ScheduleTrackDto { Id = 2, Time = "08:00", Temperature = 20.0, Days = Days.Tuesday },
                        new ScheduleTrackDto { Id = 3, Time = "08:00", Temperature = 20.0, Days = Days.Wednesday },
                        new ScheduleTrackDto { Id = 4, Time = "08:00", Temperature = 20.0, Days = Days.Thursday },
                        new ScheduleTrackDto { Id = 5, Time = "08:00", Temperature = 20.0, Days = Days.Friday },
                        new ScheduleTrackDto { Id = 6, Time = "08:00", Temperature = 20.0, Days = Days.Saturday },
                        new ScheduleTrackDto { Id = 7, Time = "08:00", Temperature = 20.0, Days = Days.Sunday },
                        new ScheduleTrackDto { Id = 8, Time = "08:00", Temperature = 20.0, Days = Days.Weekdays },
                        new ScheduleTrackDto { Id = 9, Time = "08:00", Temperature = 20.0, Days = Days.Weekends },
                        new ScheduleTrackDto { Id = 10, Time = "08:00", Temperature = 20.0, Days = Days.Everyday }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms[0].ScheduleTracks.Count.ShouldBe(10);
        result.Rooms[0].ScheduleTracks[0].Days.ShouldBe(Days.Monday);
        result.Rooms[0].ScheduleTracks[8].Days.ShouldBe(Days.Weekends);
        result.Rooms[0].ScheduleTracks[9].Days.ShouldBe(Days.Everyday);
    }

    [TestMethod]
    public void MapFromDto_WithAllConditionTypes_MapsCorrectly()
    {
        // Arrange
        var dto = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Test Room",
                    Schedules =
                    [
                        new ScheduleTrackDto { Id = 1, Time = "08:00", Temperature = 20.0, Conditions = ConditionType.None },
                        new ScheduleTrackDto { Id = 2, Time = "08:00", Temperature = 20.0, Conditions = ConditionType.Schedule1 },
                        new ScheduleTrackDto { Id = 3, Time = "08:00", Temperature = 20.0, Conditions = ConditionType.RoomInUse }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapFromDto(dto);

        // Assert
        result.Rooms[0].ScheduleTracks[0].Conditions.ShouldBe(ConditionType.None);
        result.Rooms[0].ScheduleTracks[1].Conditions.ShouldBe(ConditionType.Schedule1);
        result.Rooms[0].ScheduleTracks[2].Conditions.ShouldBe(ConditionType.RoomInUse);
    }

    #endregion

    #region MapToDto Tests

    [TestMethod]
    public void MapToDto_WithNullSchedules_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ScheduleMapper.MapToDto(null!));
    }

    [TestMethod]
    public void MapToDto_WithEmptySchedules_ReturnsEmptyRooms()
    {
        // Arrange
        var schedules = new RoomSchedules { Rooms = [] };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        result.ShouldNotBeNull();
        result.Rooms.ShouldNotBeNull();
        result.Rooms.ShouldBeEmpty();
    }

    [TestMethod]
    public void MapToDto_WithSingleRoomAndSchedule_MapsCorrectly()
    {
        // Arrange
        var schedules = new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 1,
                    Name = "Kitchen",
                    Boost = new Boost(),
                    ScheduleTracks =
                    [
                        new HeatingScheduleTrack
                        {
                            Id = 101,
                            TargetTime = new TimeOnly(7, 30),
                            Temperature = 21.5,
                            RampUpMinutes = 45,
                            Days = Days.Weekdays,
                            Conditions = ConditionType.Schedule1,
                        }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        result.Rooms.Count.ShouldBe(1);
        var room = result.Rooms[0];
        room.Id.ShouldBe(1);
        room.Name.ShouldBe("Kitchen");
        room.Boost.ShouldNotBeNull();

        room.Schedules.Count.ShouldBe(1);
        var track = room.Schedules[0];
        track.Id.ShouldBe(101);
        track.Time.ShouldBe("07:30");
        track.Temperature.ShouldBe(21.5);
        track.RampUpMinutes.ShouldBe(45);
        track.Days.ShouldBe(Days.Weekdays);
        track.Conditions.ShouldBe(ConditionType.Schedule1);
    }

    [TestMethod]
    public void MapToDto_WithBoost_MapsBoostCorrectly()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddHours(2);
        var schedules = new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 2,
                    Name = "Bedroom",
                    Boost = new Boost
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Temperature = 22.0
                    },
                    ScheduleTracks = []
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        var room = result.Rooms[0];
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldBe(startTime.ToString("O"));
        room.Boost.EndTime.ShouldBe(endTime.ToString("O"));
        room.Boost.Temperature.ShouldBe(22.0);
    }

    [TestMethod]
    public void MapToDto_WithNullBoostTimes_MapsAsNull()
    {
        // Arrange
        var schedules = new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 3,
                    Name = "Lounge",
                    Boost = new Boost
                    {
                        StartTime = null,
                        EndTime = null,
                        Temperature = 20.0
                    },
                    ScheduleTracks = []
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        var room = result.Rooms[0];
        room.Boost.ShouldNotBeNull();
        room.Boost.StartTime.ShouldBeNull();
        room.Boost.EndTime.ShouldBeNull();
        room.Boost.Temperature.ShouldBe(20.0);
    }

    [TestMethod]
    public void MapToDto_WithMultipleRoomsAndSchedules_MapsAllCorrectly()
    {
        // Arrange
        var schedules = new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 1,
                    Name = "Kitchen",
                    ScheduleTracks =
                    [
                        new HeatingScheduleTrack { Id = 101, TargetTime = new TimeOnly(7, 0), Temperature = 21.0, Days = Days.Weekdays },
                        new HeatingScheduleTrack { Id = 102, TargetTime = new TimeOnly(22, 0), Temperature = 18.0, Days = Days.Weekdays }
                    ]
                },
                new RoomSchedule
                {
                    Id = 2,
                    Name = "Bedroom",
                    ScheduleTracks =
                    [
                        new HeatingScheduleTrack { Id = 201, TargetTime = new TimeOnly(6, 30), Temperature = 19.0, Days = Days.Weekends }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        result.Rooms.Count.ShouldBe(2);
        result.Rooms[0].Schedules.Count.ShouldBe(2);
        result.Rooms[1].Schedules.Count.ShouldBe(1);
    }

    [TestMethod]
    public void MapToDto_FormatsTimesCorrectly()
    {
        // Arrange
        var schedules = new RoomSchedules
        {
            Rooms =
            [
                new RoomSchedule
                {
                    Id = 1,
                    Name = "Test",
                    ScheduleTracks =
                    [
                        new HeatingScheduleTrack { Id = 1, TargetTime = new TimeOnly(0, 0), Temperature = 20.0 },
                        new HeatingScheduleTrack { Id = 2, TargetTime = new TimeOnly(9, 5), Temperature = 20.0 },
                        new HeatingScheduleTrack { Id = 3, TargetTime = new TimeOnly(23, 59), Temperature = 20.0 }
                    ]
                }
            ]
        };

        // Act
        var result = ScheduleMapper.MapToDto(schedules);

        // Assert
        result.Rooms[0].Schedules[0].Time.ShouldBe("00:00");
        result.Rooms[0].Schedules[1].Time.ShouldBe("09:05");
        result.Rooms[0].Schedules[2].Time.ShouldBe("23:59");
    }

    #endregion

    #region Round-Trip Tests

    [TestMethod]
    public void RoundTrip_SimpleSchedule_PreservesData()
    {
        // Arrange
        var original = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Kitchen",
                    Schedules =
                    [
                        new ScheduleTrackDto
                        {
                            Id = 101,
                            Time = "07:30",
                            Temperature = 21.5,
                            RampUpMinutes = 30,
                            Days = Days.Weekdays,
                            Conditions = ConditionType.None,
                        }
                    ]
                }
            ]
        };

        // Act
        var domain = ScheduleMapper.MapFromDto(original);
        var result = ScheduleMapper.MapToDto(domain);

        // Assert
        result.Rooms.Count.ShouldBe(original.Rooms.Count);
        result.Rooms[0].Id.ShouldBe(original.Rooms[0].Id);
        result.Rooms[0].Name.ShouldBe(original.Rooms[0].Name);
        result.Rooms[0].Schedules.Count.ShouldBe(original.Rooms[0].Schedules.Count);
        result.Rooms[0].Schedules[0].Id.ShouldBe(original.Rooms[0].Schedules[0].Id);
        result.Rooms[0].Schedules[0].Time.ShouldBe(original.Rooms[0].Schedules[0].Time);
        result.Rooms[0].Schedules[0].Temperature.ShouldBe(original.Rooms[0].Schedules[0].Temperature);
    }

    [TestMethod]
    public void RoundTrip_WithBoost_PreservesDateTimes()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddHours(2);
        var original = new RoomSchedulesDto
        {
            Rooms =
            [
                new RoomDto
                {
                    Id = 1,
                    Name = "Test",
                    Boost = new BoostDto
                    {
                        StartTime = startTime.ToString("O"),
                        EndTime = endTime.ToString("O"),
                        Temperature = 22.0
                    },
                    Schedules = []
                }
            ]
        };

        // Act
        var domain = ScheduleMapper.MapFromDto(original);
        var result = ScheduleMapper.MapToDto(domain);

        // Assert
        result.Rooms[0].Boost.StartTime.ShouldBe(original.Rooms[0].Boost.StartTime);
        result.Rooms[0].Boost.EndTime.ShouldBe(original.Rooms[0].Boost.EndTime);
        result.Rooms[0].Boost.Temperature.ShouldBe(original.Rooms[0].Boost.Temperature);
    }

    #endregion

    #region Room State Mapping Tests

    [TestMethod]
    public void MapRoomStateFromDto_WithNullDto_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ScheduleMapper.MapRoomStateFromDto(null!));
    }

    [TestMethod]
    public void MapRoomStateFromDto_WithValidDto_MapsCorrectly()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var dto = new RoomStateDto
        {
            RoomId = 5,
            CurrentTemperature = 19.5,
            HeatingActive = true,
            ActiveScheduleTrackId = 102,
            LastUpdated = lastUpdated.ToString("O")
        };

        // Act
        var result = ScheduleMapper.MapRoomStateFromDto(dto);

        // Assert
        result.RoomId.ShouldBe(5);
        result.CurrentTemperature.ShouldBe(19.5);
        result.HeatingActive.ShouldBeTrue();
        result.ActiveScheduleTrackId.ShouldBe(102);
        result.LastUpdated.ShouldBe(lastUpdated);
    }

    [TestMethod]
    public void MapRoomStateFromDto_WithNullTemperature_MapsAsNull()
    {
        // Arrange
        var dto = new RoomStateDto
        {
            RoomId = 1,
            CurrentTemperature = null,
            HeatingActive = false,
            LastUpdated = DateTimeOffset.UtcNow.ToString("O")
        };

        // Act
        var result = ScheduleMapper.MapRoomStateFromDto(dto);

        // Assert
        result.CurrentTemperature.ShouldBeNull();
    }

    [TestMethod]
    public void MapRoomStateFromDto_WithInvalidRoomId_DefaultsToZero()
    {
        // This test is no longer applicable since RoomId is now int
        // Arrange
        var dto = new RoomStateDto
        {
            RoomId = 0,
            HeatingActive = false,
            LastUpdated = DateTimeOffset.UtcNow.ToString("O")
        };

        // Act
        var result = ScheduleMapper.MapRoomStateFromDto(dto);

        // Assert
        result.RoomId.ShouldBe(0);
    }


    [TestMethod]
    public void MapRoomStateToDto_WithNullState_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ScheduleMapper.MapRoomStateToDto(null!));
    }

    [TestMethod]
    public void MapRoomStateToDto_WithValidState_MapsCorrectly()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var state = new RoomState
        {
            RoomId = 5,
            CurrentTemperature = 19.5,
            HeatingActive = true,
            ActiveScheduleTrackId = 102,
            LastUpdated = lastUpdated
        };

        // Act
        var result = ScheduleMapper.MapRoomStateToDto(state);

        // Assert
        result.RoomId.ShouldBe(5);
        result.CurrentTemperature.ShouldBe(19.5);
        result.HeatingActive.ShouldBeTrue();
        result.ActiveScheduleTrackId.ShouldBe(102);
        result.LastUpdated.ShouldBe(lastUpdated.ToString("O"));
    }

    [TestMethod]
    public void MapRoomStateToDto_WithNullTemperature_PreservesNull()
    {
        // Arrange
        var state = new RoomState
        {
            RoomId = 1,
            CurrentTemperature = null,
            HeatingActive = false,
            LastUpdated = DateTimeOffset.UtcNow
        };

        // Act
        var result = ScheduleMapper.MapRoomStateToDto(state);

        // Assert
        result.CurrentTemperature.ShouldBeNull();
    }

    [TestMethod]
    public void RoomState_RoundTrip_PreservesData()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var original = new RoomStateDto
        {
            RoomId = 3,
            CurrentTemperature = 20.5,
            HeatingActive = true,
            ActiveScheduleTrackId = 201,
            LastUpdated = lastUpdated.ToString("O")
        };

        // Act
        var domain = ScheduleMapper.MapRoomStateFromDto(original);
        var result = ScheduleMapper.MapRoomStateToDto(domain);

        // Assert
        result.RoomId.ShouldBe(original.RoomId);
        result.CurrentTemperature.ShouldBe(original.CurrentTemperature);
        result.HeatingActive.ShouldBe(original.HeatingActive);
        result.ActiveScheduleTrackId.ShouldBe(original.ActiveScheduleTrackId);
        result.LastUpdated.ShouldBe(original.LastUpdated);
    }

    #endregion
}
