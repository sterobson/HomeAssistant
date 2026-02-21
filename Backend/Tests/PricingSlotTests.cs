using HomeAssistant.apps.Energy;
using HomeAssistant.Services.Energy;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class PricingSlotTests
{
    // All tests use January dates (GMT = UTC, no DST offset)
    private static readonly DateTime _testDate = new(2025, 1, 15, 10, 0, 0);

    // ========================================================================
    // FromEnergyRatesExact - Standard 30-minute rates
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_StandardOctopusRates_ProducesCorrectSlots()
    {
        // Octopus-style: 29p 00:00-14:30, 7p 14:30-19:00, 29p 19:00-00:00
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = Utc(14, 30), RateIncVat = 29.0 },
            new EnergyRate { StartTimeUtc = Utc(14, 30), EndTimeUtc = Utc(19, 0), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = Utc(19, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 29.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 15.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _testDate);

        // Should have slots at: 0 (always), 870 (14:30), 1140 (19:00) = 3 import change times
        // Export is flat so no extra change times
        slots.Count.ShouldBe(3);

        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(29.0);
        slots[0].ExportPrice.ShouldBe(15.0);

        slots[1].TimeMinutes.ShouldBe(870);  // 14:30
        slots[1].ImportPrice.ShouldBe(7.0);
        slots[1].ExportPrice.ShouldBe(15.0);

        slots[2].TimeMinutes.ShouldBe(1140); // 19:00
        slots[2].ImportPrice.ShouldBe(29.0);
        slots[2].ExportPrice.ShouldBe(15.0);
    }

    // ========================================================================
    // FromEnergyRatesExact - Non-standard change times
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_NonStandardChangeTimes_ProducesSlotsAtExactMinutes()
    {
        // Rate changes at 14:17 — not on a 30-minute boundary
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = Utc(14, 17), RateIncVat = 25.0 },
            new EnergyRate { StartTimeUtc = Utc(14, 17), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 5.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 12.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _testDate);

        slots.Count.ShouldBe(2);

        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(25.0);

        slots[1].TimeMinutes.ShouldBe(857);  // 14*60 + 17 = 857
        slots[1].ImportPrice.ShouldBe(5.0);
        slots[1].ExportPrice.ShouldBe(12.0);
    }

    // ========================================================================
    // FromEnergyRatesExact - Different import/export change times
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_DifferentImportExportChangeTimes_ProducesUnionOfChangeTimes()
    {
        // Import changes at 06:00, export changes at 09:00
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = Utc(6, 0), RateIncVat = 20.0 },
            new EnergyRate { StartTimeUtc = Utc(6, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 30.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = Utc(9, 0), RateIncVat = 10.0 },
            new EnergyRate { StartTimeUtc = Utc(9, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 18.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _testDate);

        // Union of change times: 0, 360 (06:00), 540 (09:00)
        slots.Count.ShouldBe(3);

        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(20.0);
        slots[0].ExportPrice.ShouldBe(10.0);

        slots[1].TimeMinutes.ShouldBe(360);  // 06:00
        slots[1].ImportPrice.ShouldBe(30.0);
        slots[1].ExportPrice.ShouldBe(10.0); // export hasn't changed yet

        slots[2].TimeMinutes.ShouldBe(540);  // 09:00
        slots[2].ImportPrice.ShouldBe(30.0); // import unchanged
        slots[2].ExportPrice.ShouldBe(18.0);
    }

    // ========================================================================
    // FromEnergyRatesExact - Empty rates
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_EmptyRates_ProducesNoSlots()
    {
        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact([], [], _testDate);

        // No rate data means no slots — avoids phantom 0-price entries
        slots.Count.ShouldBe(0);
    }

    [TestMethod]
    public void FromEnergyRatesExact_EmptyImportRates_ExportStillPopulated()
    {
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 15.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact([], exportRates, _testDate);

        slots.Count.ShouldBe(1);
        slots[0].ImportPrice.ShouldBe(0);     // no import rate → 0
        slots[0].ExportPrice.ShouldBe(15.0);  // export rate found
    }

    // ========================================================================
    // FromEnergyRatesExact - Rate gap (no coverage at a time)
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_RateGap_DefaultsToZero()
    {
        // Import rate only covers 06:00-12:00, leaving gaps before and after
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = Utc(6, 0), EndTimeUtc = Utc(12, 0), RateIncVat = 20.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 10.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _testDate);

        // Change times: 0 (always), 360 (06:00)
        slots.Count.ShouldBe(2);

        // Before coverage: import defaults to 0
        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(0);
        slots[0].ExportPrice.ShouldBe(10.0);

        // During coverage: import has the rate
        slots[1].TimeMinutes.ShouldBe(360);
        slots[1].ImportPrice.ShouldBe(20.0);
        slots[1].ExportPrice.ShouldBe(10.0);
    }

    // ========================================================================
    // FromEnergyRatesExact - Rates from other days are excluded
    // ========================================================================

    [TestMethod]
    public void FromEnergyRatesExact_RatesFromOtherDays_Excluded()
    {
        // Rate starts on a different day — should not produce a slot
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = Utc(0, 0), EndTimeUtc = UtcNextDay(0, 0), RateIncVat = 25.0 },
            // This rate starts tomorrow — should be excluded
            new EnergyRate { StartTimeUtc = UtcNextDay(0, 0), EndTimeUtc = UtcNextDay(12, 0), RateIncVat = 99.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, [], _testDate);

        // Only minute 0 from today's rate
        slots.Count.ShouldBe(1);
        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(25.0);
    }

    // ========================================================================
    // ExtendWithNextDay
    // ========================================================================

    [TestMethod]
    public void ExtendWithNextDay_BasicExtension_AppendsTomorrowShiftedBy1440()
    {
        List<PricingSlot> today =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29.9, ExportPrice = 15 },
            new() { TimeMinutes = 1410, ImportPrice = 7, ExportPrice = 5 }
        ];
        List<PricingSlot> tomorrow =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29.9, ExportPrice = 15 }
        ];

        List<PricingSlot> result = PricingSlot.ExtendWithNextDay(today, tomorrow);

        result.Count.ShouldBe(5);
        // Today's slots unchanged
        result[0].TimeMinutes.ShouldBe(0);
        result[0].ImportPrice.ShouldBe(7);
        result[1].TimeMinutes.ShouldBe(330);
        result[1].ImportPrice.ShouldBe(29.9);
        result[2].TimeMinutes.ShouldBe(1410);
        result[2].ImportPrice.ShouldBe(7);
        // Tomorrow's slots shifted by +1440
        result[3].TimeMinutes.ShouldBe(1440);
        result[3].ImportPrice.ShouldBe(7);
        result[3].ExportPrice.ShouldBe(5);
        result[4].TimeMinutes.ShouldBe(1770);
        result[4].ImportPrice.ShouldBe(29.9);
        result[4].ExportPrice.ShouldBe(15);
    }

    [TestMethod]
    public void ExtendWithNextDay_EmptyTomorrow_ReturnsCopyOfToday()
    {
        List<PricingSlot> today =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 330, ImportPrice = 30, ExportPrice = 15 }
        ];

        List<PricingSlot> result = PricingSlot.ExtendWithNextDay(today, []);

        result.Count.ShouldBe(2);
        result[0].TimeMinutes.ShouldBe(0);
        result[1].TimeMinutes.ShouldBe(330);
    }

    [TestMethod]
    public void ExtendWithNextDay_DoesNotMutatOriginalLists()
    {
        List<PricingSlot> today = [new() { TimeMinutes = 0, ImportPrice = 10, ExportPrice = 5 }];
        List<PricingSlot> tomorrow = [new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 }];

        List<PricingSlot> result = PricingSlot.ExtendWithNextDay(today, tomorrow);

        // Original lists unchanged
        today.Count.ShouldBe(1);
        tomorrow.Count.ShouldBe(1);
        tomorrow[0].TimeMinutes.ShouldBe(0); // not shifted

        // Result is independent
        result.Count.ShouldBe(2);
        result[1].TimeMinutes.ShouldBe(1440);
    }

    // ========================================================================
    // FromEnergyRatesExact - BST (British Summer Time, UTC+1)
    // These tests only run on UK-timezone machines.
    // ========================================================================

    private static readonly DateTime _bstDate = new(2025, 6, 15, 10, 0, 0);

    private static bool IsUkTimezone()
    {
        // Check that local timezone observes BST in June (UTC+1)
        DateTimeOffset june = new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(june);
        return offset == TimeSpan.FromHours(1);
    }

    [TestMethod]
    public void FromEnergyRatesExact_BstDay_RateTimesShiftedByOneHour()
    {
        if (!IsUkTimezone()) { Assert.Inconclusive("Test requires UK timezone (BST)"); return; }

        // Octopus-style rates published in UTC:
        // 00:00 UTC (01:00 BST) to 05:30 UTC (06:30 BST) at 7p
        // 05:30 UTC (06:30 BST) to 23:00 UTC (00:00 BST next day) at 29p
        List<EnergyRate> importRates =
        [
            new EnergyRate { StartTimeUtc = BstUtc(0, 0), EndTimeUtc = BstUtc(5, 30), RateIncVat = 7.0 },
            new EnergyRate { StartTimeUtc = BstUtc(5, 30), EndTimeUtc = BstUtcNextDay(0, 0), RateIncVat = 29.0 }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate { StartTimeUtc = BstUtc(0, 0), EndTimeUtc = BstUtcNextDay(0, 0), RateIncVat = 15.0 }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _bstDate);

        // In BST: day starts at 2025-06-14 23:00 UTC
        // Rate at 00:00 UTC = 01:00 BST = minute 60 (but day starts at 00:00 BST so it's 60)
        // Actually: dayStart is June 15 00:00 local (BST), dayStartUtc = June 14 23:00 UTC
        // Rate at June 15 00:00 UTC starts at 01:00 BST = minute 60
        // But the rate at June 14 23:00 UTC starts at 00:00 BST = minute 0 — THAT'S the 7p rate start
        // Wait, the first rate starts at BstUtc(0,0) = June 15 00:00 UTC = June 15 01:00 BST = minute 60
        // But dayStartUtc = June 14 23:00 UTC. Rate at 00:00 UTC >= 23:00 UTC (previous day) → included.
        // localTime = ConvertFromUtc(June 15 00:00 UTC) = June 15 01:00 BST
        // minutes = 01:00 BST - 00:00 BST = 60

        // So: change at minute 0 (always), and minute 60 won't show because 7p rate covers from before midnight
        // Actually the 7p rate starts at 00:00 UTC = 01:00 BST. dayStartUtc = 14 June 23:00 UTC.
        // rate.StartTimeUtc (June 15 00:00 UTC) >= dayStartUtc (June 14 23:00 UTC) → included
        // localTime = June 15 01:00 BST, minutes = 60
        // The 29p rate starts at 05:30 UTC = 06:30 BST, minutes = 390

        // Minute 0 (00:00-01:00 BST) has no rate coverage — skipped to avoid phantom 0-price entry
        slots.Count.ShouldBe(2); // minute 60, minute 390

        slots[0].TimeMinutes.ShouldBe(60); // 01:00 BST
        slots[0].ImportPrice.ShouldBe(7.0);

        slots[1].TimeMinutes.ShouldBe(390); // 06:30 BST
        slots[1].ImportPrice.ShouldBe(29.0);
    }

    [TestMethod]
    public void FromEnergyRatesExact_BstDay_RateSpanningMidnightBst_CorrectlyCoversEarlyMorning()
    {
        if (!IsUkTimezone()) { Assert.Inconclusive("Test requires UK timezone (BST)"); return; }

        // Rate that starts before midnight BST (in UTC terms: June 14 22:30 UTC = June 14 23:30 BST)
        // and runs past midnight BST into the target day
        // This simulates an overnight cheap rate: 23:30 BST to 05:30 BST
        List<EnergyRate> importRates =
        [
            // Previous day rate spanning into target day: 22:30 UTC June 14 = 23:30 BST June 14
            new EnergyRate
            {
                StartTimeUtc = new DateTime(2025, 6, 14, 22, 30, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2025, 6, 15, 4, 30, 0, DateTimeKind.Utc), // 05:30 BST
                RateIncVat = 7.0
            },
            // Day rate: 04:30 UTC = 05:30 BST to 22:30 UTC = 23:30 BST
            new EnergyRate
            {
                StartTimeUtc = new DateTime(2025, 6, 15, 4, 30, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2025, 6, 15, 22, 30, 0, DateTimeKind.Utc),
                RateIncVat = 29.0
            }
        ];
        List<EnergyRate> exportRates =
        [
            new EnergyRate
            {
                StartTimeUtc = new DateTime(2025, 6, 14, 23, 0, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2025, 6, 15, 23, 0, 0, DateTimeKind.Utc),
                RateIncVat = 15.0
            }
        ];

        List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(importRates, exportRates, _bstDate);

        // dayStartUtc = June 14 23:00 UTC (= June 15 00:00 BST)
        // dayEndUtc = June 15 23:00 UTC (= June 16 00:00 BST)
        // The 7p rate starts at June 14 22:30 UTC — before dayStartUtc, so NOT included as a change time
        // The 29p rate starts at June 15 04:30 UTC = 05:30 BST = minute 330 — included
        // Always includes minute 0

        slots.Count.ShouldBe(2); // minute 0, minute 330

        // At minute 0 (00:00 BST = 23:00 UTC June 14):
        // The 7p rate covers 22:30-04:30 UTC, and 23:00 UTC is within that → 7p
        slots[0].TimeMinutes.ShouldBe(0);
        slots[0].ImportPrice.ShouldBe(7.0);
        slots[0].ExportPrice.ShouldBe(15.0);

        // At minute 330 (05:30 BST = 04:30 UTC):
        slots[1].TimeMinutes.ShouldBe(330);
        slots[1].ImportPrice.ShouldBe(29.0);
        slots[1].ExportPrice.ShouldBe(15.0);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static DateTime Utc(int hour, int minute) =>
        new(2025, 1, 15, hour, minute, 0, DateTimeKind.Utc);

    private static DateTime UtcNextDay(int hour, int minute) =>
        new(2025, 1, 16, hour, minute, 0, DateTimeKind.Utc);

    private static DateTime BstUtc(int hour, int minute) =>
        new(2025, 6, 15, hour, minute, 0, DateTimeKind.Utc);

    private static DateTime BstUtcNextDay(int hour, int minute) =>
        new(2025, 6, 16, hour, minute, 0, DateTimeKind.Utc);
}
