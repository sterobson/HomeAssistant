using HomeAssistant.Shared.Climate;
using Shouldly;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// The only things a guest with a QR link can do, for a zone or a whole layout group. The point of these tests
/// is the blast radius: an override may change those zones' boosts and nothing else in the schedules.
/// </summary>
[TestClass]
public sealed class GuestOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(16.0, 60, true, DisplayName = "lowest temperature")]
    [DataRow(24.0, 360, true, DisplayName = "highest temperature, longest duration")]
    [DataRow(15.5, 60, false, DisplayName = "too cold")]
    [DataRow(24.5, 60, false, DisplayName = "too hot")]
    [DataRow(20.0, 0, false, DisplayName = "no duration")]
    [DataRow(20.0, 361, false, DisplayName = "over six hours")]
    [DataRow(double.NaN, 60, false, DisplayName = "not a number")]
    public void Validate_EnforcesTheGuestLimits(double temperature, int minutes, bool allowed)
    {
        (GuestOverride.Validate(temperature, minutes) == null).ShouldBe(allowed);
    }

    [TestMethod]
    public void Apply_ChangesOnlyThoseZonesBoosts()
    {
        RoomSchedulesDto schedules = SampleHouse();
        string before = Serialise(schedules);

        GuestOverride.Apply(schedules, [2], temperature: 21, durationMinutes: 90, Now).ShouldBe(1);

        BoostDto boost = schedules.Rooms.Single(r => r.Id == 2).Boost!;
        boost.Temperature.ShouldBe(21);
        DateTimeOffset.Parse(boost.StartTime!).ShouldBe(Now);
        DateTimeOffset.Parse(boost.EndTime!).ShouldBe(Now.AddMinutes(90));

        // Put zone 2's boost back the way it was, and the whole file must match what it was before.
        schedules.Rooms.Single(r => r.Id == 2).Boost = null;
        Serialise(schedules).ShouldBe(before, "schedules, layout, occupancy and other zones are untouched");
    }

    [TestMethod]
    public void Apply_AgainChangesARunningOverride()
    {
        // A guest who set 24 °C for 6 hours can change their mind an hour later.
        RoomSchedulesDto schedules = SampleHouse();
        GuestOverride.Apply(schedules, [2], 24, 360, Now);

        GuestOverride.Apply(schedules, [2], 21, 120, Now.AddHours(1));

        BoostDto boost = schedules.Rooms.Single(r => r.Id == 2).Boost!;
        boost.Temperature.ShouldBe(21);
        DateTimeOffset.Parse(boost.EndTime!).ShouldBe(Now.AddHours(3));
    }

    [TestMethod]
    public void Apply_UnknownZone_ChangesNothing()
    {
        RoomSchedulesDto schedules = SampleHouse();
        string before = Serialise(schedules);

        GuestOverride.Apply(schedules, [99], 20, 60, Now).ShouldBe(0);

        Serialise(schedules).ShouldBe(before);
    }

    [TestMethod]
    public void Cancel_ClearsOnlyThoseZones()
    {
        RoomSchedulesDto schedules = SampleHouse();
        schedules.Rooms.Single(r => r.Id == 1).Boost = new BoostDto { Temperature = 22, EndTime = Now.AddHours(1).ToString("O") };
        GuestOverride.Apply(schedules, [2], 21, 60, Now);

        GuestOverride.Cancel(schedules, [2]).ShouldBe(1);

        schedules.Rooms.Single(r => r.Id == 2).Boost.ShouldBeNull();
        schedules.Rooms.Single(r => r.Id == 1).Boost!.Temperature.ShouldBe(22, "another zone's boost is not the guest's to cancel");
        GuestOverride.Cancel(schedules, [2]).ShouldBe(0, "nothing left to cancel");
    }

    [TestMethod]
    public void AGroupCoversEveryZoneInsideIt_AtAnyDepth()
    {
        RoomSchedulesDto schedules = SampleHouse();

        GuestOverride.RoomIdsInGroup(schedules, "downstairs").ShouldBe([1, 2, 3], ignoreOrder: true);
        GuestOverride.RoomIdsInGroup(schedules, "den").ShouldBe([2, 3], ignoreOrder: true);
        GuestOverride.RoomIdsInGroup(schedules, "gone").ShouldBeEmpty("a deleted group's link must stop working");
    }

    [TestMethod]
    public void AGroupOverride_SetsEveryZoneInTheGroup_AndNothingOutsideIt()
    {
        RoomSchedulesDto schedules = SampleHouse();
        schedules.Rooms.Add(new RoomDto { Id = 4, Name = "bedroom 1" });

        GuestOverride.Apply(schedules, GuestOverride.RoomIdsInGroup(schedules, "den"), 20, 60, Now).ShouldBe(2);

        schedules.Rooms.Where(r => r.Boost != null).Select(r => r.Id).ShouldBe([2, 3], ignoreOrder: true);
    }

    [TestMethod]
    public void AGroupSkipsAZoneThatNoLongerExists()
    {
        RoomSchedulesDto schedules = SampleHouse();
        schedules.Rooms.RemoveAll(r => r.Id == 3);

        GuestOverride.RoomIdsInGroup(schedules, "den").ShouldBe([2]);
    }

    [TestMethod]
    public void Active_IgnoresAnExpiredBoost()
    {
        RoomDto room = new() { Id = 1, Boost = new BoostDto { Temperature = 21, EndTime = Now.AddMinutes(-1).ToString("O") } };
        GuestOverride.Active(room, Now).ShouldBeNull();

        room.Boost.EndTime = Now.AddMinutes(1).ToString("O");
        GuestOverride.Active(room, Now).ShouldNotBeNull();
    }

    [TestMethod]
    public void TheBoostItWritesIsReadBackByTheHeatingApp()
    {
        RoomSchedulesDto schedules = SampleHouse();
        GuestOverride.Apply(schedules, [2], 21, 60, DateTimeOffset.UtcNow);

        HomeAssistant.Services.Climate.RoomSchedules mapped = HomeAssistant.Services.ScheduleMapper.MapFromDto(schedules);

        HomeAssistant.Services.Climate.Boost boost = mapped.Rooms.Single(r => r.Id == 2).Boost;
        boost.Temperature.ShouldBe(21);
        boost.EndTime!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    private static RoomSchedulesDto SampleHouse()
    {
        return new RoomSchedulesDto
        {
            HouseOccupancyState = HouseOccupancyState.Home,
            Layout =
            [
                new RoomLayoutNodeDto
                {
                    Id = "downstairs", Name = "Downstairs", Children =
                    [
                        new() { RoomId = 1 },
                        new() { Id = "den", Name = "Den", Children = [new() { RoomId = 2 }, new() { RoomId = 3 }] }
                    ]
                }
            ],
            Rooms =
            [
                new RoomDto { Id = 1, Name = "kitchen", Schedules = [new ScheduleTrackDto { Id = 1, Time = "06:30", Temperature = 18 }] },
                new RoomDto { Id = 2, Name = "den", DisplayName = "Den", Icon = "📺", Schedules = [new ScheduleTrackDto { Id = 1, Time = "07:00", Temperature = 19 }] },
                new RoomDto { Id = 3, Name = "games desk", DisplayName = "Ste's desk", Schedules = [new ScheduleTrackDto { Id = 1, Time = "07:00", Temperature = 21 }] }
            ]
        };
    }

    private static string Serialise(RoomSchedulesDto schedules) => JsonSerializer.Serialize(schedules);
}
