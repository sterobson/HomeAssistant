using HomeAssistant.Functions.Services;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The daily battery record keeps one row per device per day, and relies entirely on the key
/// derivation to do it: the app pushes every few hours and on every restart, so if two pushes
/// on one day produced different row keys the table would fill with duplicates and any "when
/// did this battery start dropping?" reading of it would be wrong.
/// </summary>
[TestClass]
public sealed class DeviceBatteryStorageKeyTests
{
    [TestMethod]
    public void TwoPushesOnTheSameDayShareARowKey()
    {
        // A scheduled push, and another after an over-the-air restart that evening.
        DateTimeOffset morning = new DateTimeOffset(2026, 10, 19, 7, 15, 0, TimeSpan.Zero);
        DateTimeOffset evening = new DateTimeOffset(2026, 10, 19, 22, 40, 0, TimeSpan.Zero);

        DeviceBatteryStorageService.GenerateRowKey(morning)
            .ShouldBe(DeviceBatteryStorageService.GenerateRowKey(evening));
    }

    [TestMethod]
    public void DifferentDaysGetDifferentRowKeys()
    {
        DateTimeOffset today = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset yesterday = today.AddDays(-1);

        DeviceBatteryStorageService.GenerateRowKey(today)
            .ShouldNotBe(DeviceBatteryStorageService.GenerateRowKey(yesterday));
    }

    [TestMethod]
    public void NewerDaysSortFirst()
    {
        // Inverted ticks, matching the temperature history convention: the newest row is the
        // smallest key, so the latest reading is the first row of a partition scan.
        DateTimeOffset today = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastWeek = today.AddDays(-7);

        string todayKey = DeviceBatteryStorageService.GenerateRowKey(today);
        string lastWeekKey = DeviceBatteryStorageService.GenerateRowKey(lastWeek);

        string.CompareOrdinal(todayKey, lastWeekKey).ShouldBeLessThan(0);
    }

    [TestMethod]
    public void RowKeysAreFixedWidthSoOrdinalComparisonIsChronological()
    {
        // Without zero padding, ordinal string comparison on numbers of differing length
        // would order the table wrongly and the range filters would silently miss rows.
        DeviceBatteryStorageService.GenerateRowKey(DateTimeOffset.UtcNow).Length.ShouldBe(19);
    }

    [TestMethod]
    public void ALateEveningLocalPushLandsOnTheUtcDay()
    {
        // 00:30 British Summer Time is 23:30 UTC the day before. Both the push guard and the
        // storage use UTC, so they agree - this test pins that they do.
        DateTimeOffset justAfterLocalMidnight = new DateTimeOffset(2026, 7, 19, 0, 30, 0, TimeSpan.FromHours(1));

        DeviceBatteryStorageService.ToUtcDate(justAfterLocalMidnight)
            .ShouldBe(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public void PartitionKeysSeparateDevicesAndHouses()
    {
        DeviceBatteryStorageService.BuildPartitionKey("1234567", "sensor.bedroom_1_radiator_battery")
            .ShouldBe("1234567_sensor.bedroom_1_radiator_battery");

        DeviceBatteryStorageService.BuildPartitionKey("1234567", "sensor.a")
            .ShouldNotBe(DeviceBatteryStorageService.BuildPartitionKey("1701", "sensor.a"));
    }
}
