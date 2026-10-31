using HomeAssistant.Services.Climate;
using Shouldly;
using System.Text.Json;

namespace HomeAssistant.Tests;

[TestClass]
public class ThermometerReportsTests
{
    private const string Hallway = "sensor.hallway_climate_temperature";
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 17, 38, 0, TimeSpan.Zero);

    // The shape of a zha/devices answer, cut down to what is read: last_seen has no time zone.
    private const string Answer = """
        [
          { "last_seen": "2026-10-31T17:37:15",
            "entities": [ { "entity_id": "sensor.hallway_climate_temperature" }, { "entity_id": "sensor.hallway_climate_humidity" } ] },
          { "last_seen": null, "entities": [ { "entity_id": "sensor.never_seen" } ] },
          { "last_seen": "2026-10-31T17:30:00" }
        ]
        """;

    [TestMethod]
    public void EachEntity_TakesItsDevicesLastSeen()
    {
        Dictionary<string, string> lastSeen = ThermometerReports.ParseLastSeen(JsonDocument.Parse(Answer).RootElement);

        lastSeen[Hallway].ShouldBe("2026-10-31T17:37:15");
        lastSeen["sensor.hallway_climate_humidity"].ShouldBe("2026-10-31T17:37:15");
        lastSeen.ContainsKey("sensor.never_seen").ShouldBeFalse();
        lastSeen.Count.ShouldBe(2);
    }

    [TestMethod]
    public void FirstSight_IsReadAsUtc()
    {
        Dictionary<string, (string, DateTimeOffset)> heard = new();

        ThermometerReports.Observe(heard, Hallway, "2026-10-31T16:00:00", Now).ShouldBe(new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public void FirstSight_InTheFuture_IsTakenAsNow()
    {
        // In summer time ZHA's local wall clock would read an hour ahead of UTC.
        Dictionary<string, (string, DateTimeOffset)> heard = new();

        ThermometerReports.Observe(heard, Hallway, "2026-10-31T18:37:00", Now).ShouldBe(Now);
    }

    [TestMethod]
    public void AnUnchangedLastSeen_KeepsWhenItWasHeardFrom_AndAChangedOneIsNow()
    {
        Dictionary<string, (string, DateTimeOffset)> heard = new();
        ThermometerReports.Observe(heard, Hallway, "2026-10-31T17:37:15", Now);

        ThermometerReports.Observe(heard, Hallway, "2026-10-31T17:37:15", Now.AddMinutes(90)).ShouldBe(new DateTimeOffset(2026, 10, 31, 17, 37, 15, TimeSpan.Zero));
        // Whatever zone the new value is in, it changed, so the device has just been heard from.
        ThermometerReports.Observe(heard, Hallway, "2026-10-31T20:07:30", Now.AddMinutes(91)).ShouldBe(Now.AddMinutes(91));
    }

    [TestMethod]
    public void AnUnreadableLastSeen_IsTakenAsNow()
    {
        Dictionary<string, (string, DateTimeOffset)> heard = new();

        ThermometerReports.Observe(heard, Hallway, "yesterday", Now).ShouldBe(Now);
    }
}
