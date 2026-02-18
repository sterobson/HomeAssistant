using HomeAssistant.Services.Energy;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class PriceAnalysisTests
{
    // ========================================================================
    // FindExportExceedsImportCrossovers
    // ========================================================================

    [TestMethod]
    public void FindExportExceedsImport_SingleCrossover_FindsIt()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 10, ExportPrice = 20 }, // crossover here
            new() { TimeMinutes = 90, ImportPrice = 10, ExportPrice = 20 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots);

        result.Count.ShouldBe(1);
        result[0].TimeMinutes.ShouldBe(60);
    }

    [TestMethod]
    public void FindExportExceedsImport_MultipleCrossovers_FindsAll()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 20 },   // first crossover
            new() { TimeMinutes = 60, ImportPrice = 30, ExportPrice = 10 },   // back to import > export
            new() { TimeMinutes = 90, ImportPrice = 10, ExportPrice = 20 },   // second crossover
            new() { TimeMinutes = 120, ImportPrice = 10, ExportPrice = 20 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots);

        result.Count.ShouldBe(2);
        result[0].TimeMinutes.ShouldBe(30);
        result[1].TimeMinutes.ShouldBe(90);
    }

    [TestMethod]
    public void FindExportExceedsImport_NoCrossover_ReturnsEmpty()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 25, ExportPrice = 10 },
            new() { TimeMinutes = 60, ImportPrice = 20, ExportPrice = 10 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots);

        result.ShouldBeEmpty();
    }

    [TestMethod]
    public void FindExportExceedsImport_EqualPricesNotACrossover()
    {
        // When export == import, export does NOT exceed import
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 15, ExportPrice = 15 }, // equal, not exceeding
            new() { TimeMinutes = 60, ImportPrice = 20, ExportPrice = 10 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots);

        result.ShouldBeEmpty();
    }

    [TestMethod]
    public void FindExportExceedsImport_WithAbsoluteThreshold()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 12 }, // export > import, but not by threshold
            new() { TimeMinutes = 60, ImportPrice = 10, ExportPrice = 25 }, // export > import by more than threshold
        ];

        // Threshold of 5 means export must exceed import by 500 (threshold * 100)
        // Wait, looking at PriceAnalysis code: absolute threshold uses thresholdValue * 100
        // So threshold 0.05 means export must exceed import + 5
        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots, "absolute", 0.05);

        // At slot 30: export 12 > import 10 + 5? 12 > 15? No
        // At slot 60: export 25 > import 10 + 5? 25 > 15? Yes
        result.Count.ShouldBe(1);
        result[0].TimeMinutes.ShouldBe(60);
    }

    [TestMethod]
    public void FindExportExceedsImport_WithPercentThreshold()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 11 }, // export > import by 10%, threshold is 50%
            new() { TimeMinutes = 60, ImportPrice = 10, ExportPrice = 20 }, // export > import by 100%
        ];

        List<PricingSlot> result = PriceAnalysis.FindExportExceedsImportCrossovers(slots, "percent", 50);

        // At slot 30: 11 > 10 * 1.5? 11 > 15? No
        // At slot 60: 20 > 10 * 1.5? 20 > 15? Yes
        result.Count.ShouldBe(1);
        result[0].TimeMinutes.ShouldBe(60);
    }

    // ========================================================================
    // FindImportExceedsExportCrossovers
    // ========================================================================

    [TestMethod]
    public void FindImportExceedsExport_SingleCrossover_FindsIt()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10, ExportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 30, ExportPrice = 10 }, // crossover here
            new() { TimeMinutes = 90, ImportPrice = 30, ExportPrice = 10 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindImportExceedsExportCrossovers(slots);

        result.Count.ShouldBe(1);
        result[0].TimeMinutes.ShouldBe(60);
    }

    [TestMethod]
    public void FindImportExceedsExport_NoCrossover_ReturnsEmpty()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 30, ExportPrice = 10 },
        ];

        List<PricingSlot> result = PriceAnalysis.FindImportExceedsExportCrossovers(slots);

        result.ShouldBeEmpty();
    }

    // ========================================================================
    // FindPeaks
    // ========================================================================

    [TestMethod]
    public void FindPeaks_SinglePeak_FindsIt()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 40 },
            new() { TimeMinutes = 90, ImportPrice = 20 },
            new() { TimeMinutes = 120, ImportPrice = 10 },
        ];

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.Count.ShouldBe(1);
        peaks[0].TimeMinutes.ShouldBe(60);
    }

    [TestMethod]
    public void FindPeaks_PlateauPeak_ReturnsMidpoint()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 40 },
            new() { TimeMinutes = 60, ImportPrice = 40 },
            new() { TimeMinutes = 90, ImportPrice = 40 },
            new() { TimeMinutes = 120, ImportPrice = 10 },
        ];

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.Count.ShouldBe(1);
        peaks[0].TimeMinutes.ShouldBe(60); // midpoint of plateau indices 1-3
    }

    [TestMethod]
    public void FindPeaks_MultiplePeaks_FindsAll()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 30 },  // first peak
            new() { TimeMinutes = 60, ImportPrice = 10 },
            new() { TimeMinutes = 90, ImportPrice = 40 },  // second peak
            new() { TimeMinutes = 120, ImportPrice = 10 },
        ];

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.Count.ShouldBe(2);
        peaks[0].TimeMinutes.ShouldBe(30);
        peaks[1].TimeMinutes.ShouldBe(90);
    }

    [TestMethod]
    public void FindPeaks_FlatPricing_ReturnsEmpty()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 20 },
        ];

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.ShouldBeEmpty();
    }

    [TestMethod]
    public void FindPeaks_SingleSlot_ReturnsEmpty()
    {
        List<PricingSlot> slots = [new() { TimeMinutes = 0, ImportPrice = 20 }];

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.ShouldBeEmpty();
    }

    // ========================================================================
    // FindTroughs
    // ========================================================================

    [TestMethod]
    public void FindTroughs_SingleTrough_FindsIt()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 5 },
            new() { TimeMinutes = 90, ImportPrice = 20 },
            new() { TimeMinutes = 120, ImportPrice = 30 },
        ];

        List<PricingSlot> troughs = PriceAnalysis.FindTroughs(slots, s => s.ImportPrice);

        troughs.Count.ShouldBe(1);
        troughs[0].TimeMinutes.ShouldBe(60);
    }

    [TestMethod]
    public void FindTroughs_PlateauTrough_ReturnsMidpoint()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 5 },
            new() { TimeMinutes = 60, ImportPrice = 5 },
            new() { TimeMinutes = 90, ImportPrice = 5 },
            new() { TimeMinutes = 120, ImportPrice = 30 },
        ];

        List<PricingSlot> troughs = PriceAnalysis.FindTroughs(slots, s => s.ImportPrice);

        troughs.Count.ShouldBe(1);
        troughs[0].TimeMinutes.ShouldBe(60); // midpoint
    }

    [TestMethod]
    public void FindTroughs_MultipleTroughs_FindsAll()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30 },
            new() { TimeMinutes = 30, ImportPrice = 5 },   // first trough
            new() { TimeMinutes = 60, ImportPrice = 30 },
            new() { TimeMinutes = 90, ImportPrice = 10 },  // second trough
            new() { TimeMinutes = 120, ImportPrice = 30 },
        ];

        List<PricingSlot> troughs = PriceAnalysis.FindTroughs(slots, s => s.ImportPrice);

        troughs.Count.ShouldBe(2);
        troughs[0].TimeMinutes.ShouldBe(30);
        troughs[1].TimeMinutes.ShouldBe(90);
    }

    // ========================================================================
    // FindMinimaRegionBoundaries
    // ========================================================================

    [TestMethod]
    public void FindMinimaRegionBoundaries_SingleRegion_ReturnsStartAndEnd()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 30, ExportPrice = 15 },
            new() { TimeMinutes = 30, ImportPrice = 30, ExportPrice = 15 },
            new() { TimeMinutes = 60, ImportPrice = 5, ExportPrice = 15 },
            new() { TimeMinutes = 90, ImportPrice = 5, ExportPrice = 15 },
            new() { TimeMinutes = 120, ImportPrice = 30, ExportPrice = 15 },
            new() { TimeMinutes = 150, ImportPrice = 30, ExportPrice = 15 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.Count.ShouldBe(2);
        PriceBoundary start = boundaries.First(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.First(b => b.BoundaryType == "end");

        start.TimeMinutes.ShouldBe(60);
        end.TimeMinutes.ShouldBe(120);
    }

    [TestMethod]
    public void FindMinimaRegionBoundaries_FlatPricing_ReturnsEmpty()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 20 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.ShouldBeEmpty();
    }

    [TestMethod]
    public void FindMinimaRegionBoundaries_LowPriceAtEdge_DetectedWhenBelowAverage()
    {
        // With the average-based algorithm, a low price at the edge IS detected
        // because it falls below 75% of the day's average.
        // Average = (30*5 + 30*20 + (1440-60)*30) / 1440 ≈ 29.27, threshold ≈ 21.95
        // Both 5p and 20p are below the threshold → both are in the cheap region
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 5 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.Count.ShouldBe(2);
        PriceBoundary start = boundaries.First(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.First(b => b.BoundaryType == "end");
        start.TimeMinutes.ShouldBe(0);
        end.TimeMinutes.ShouldBe(60);
    }

    // ========================================================================
    // FindMaximaRegionBoundaries
    // ========================================================================

    [TestMethod]
    public void FindMaximaRegionBoundaries_SingleRegion_ReturnsStartAndEnd()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10, ExportPrice = 15 },
            new() { TimeMinutes = 30, ImportPrice = 10, ExportPrice = 15 },
            new() { TimeMinutes = 60, ImportPrice = 45, ExportPrice = 15 },
            new() { TimeMinutes = 90, ImportPrice = 45, ExportPrice = 15 },
            new() { TimeMinutes = 120, ImportPrice = 10, ExportPrice = 15 },
            new() { TimeMinutes = 150, ImportPrice = 10, ExportPrice = 15 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.Count.ShouldBe(2);
        PriceBoundary start = boundaries.First(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.First(b => b.BoundaryType == "end");

        start.TimeMinutes.ShouldBe(60);
        end.TimeMinutes.ShouldBe(120);
    }

    [TestMethod]
    public void FindMaximaRegionBoundaries_FlatPricing_ReturnsEmpty()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 20 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.ShouldBeEmpty();
    }

    [TestMethod]
    public void FindMinimaRegionBoundaries_MaximaAtEdge_NotDetected()
    {
        // A maximum at the end doesn't have both neighbours
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 10 },
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 40 },  // high at end - no next
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.ShouldBeEmpty();
    }

    // ========================================================================
    // ========================================================================
    // CalculateAveragePrice
    // ========================================================================

    [TestMethod]
    public void CalculateAveragePrice_CozyTariff_ReturnsWeightedAverage()
    {
        // Cozy: 0:00-2:00 27.5p, 2:00-5:00 16.5p, 5:00-16:00 27.5p, 16:00-19:00 38.5p, 19:00-0:00 27.5p
        // Weighted: (120×27.5 + 180×16.5 + 660×27.5 + 180×38.5 + 300×27.5) / 1440 = 27.5
        List<PricingSlot> slots = BuildCozyTariffSlots();

        double? avg = PriceAnalysis.CalculateAveragePrice(slots, s => s.ImportPrice);

        avg.ShouldNotBeNull();
        avg.Value.ShouldBe(27.5, 0.01);
    }

    [TestMethod]
    public void CalculateAveragePrice_CozyTariffWithExport_ReturnsCorrectExportAverage()
    {
        // Export prices: 0:00-2:00 9.7, 2:00-5:00 4.2, 5:00-16:00 9.7, 16:00-19:00 27.7, 19:00-0:00 9.7
        // Weighted: (120×9.7 + 180×4.2 + 660×9.7 + 180×27.7 + 300×9.7) / 1440 ≈ 11.2625
        List<PricingSlot> slots = [];
        for (int minutes = 0; minutes < 1440; minutes += 30)
        {
            (double importPrice, double exportPrice) = minutes switch
            {
                >= 0 and < 120 => (27.5, 9.7),
                >= 120 and < 300 => (16.5, 4.2),
                >= 300 and < 960 => (27.5, 9.7),
                >= 960 and < 1140 => (38.5, 27.7),
                _ => (27.5, 9.7)
            };
            slots.Add(new PricingSlot { TimeMinutes = minutes, ImportPrice = importPrice, ExportPrice = exportPrice });
        }

        double? avg = PriceAnalysis.CalculateAveragePrice(slots, s => s.ExportPrice);

        avg.ShouldNotBeNull();
        avg.Value.ShouldBe(11.2625, 0.01);
    }

    [TestMethod]
    public void CalculateAveragePrice_FlatPricing_ReturnsExactValue()
    {
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 20 },
            new() { TimeMinutes = 720, ImportPrice = 20 },
        ];

        double? avg = PriceAnalysis.CalculateAveragePrice(slots, s => s.ImportPrice);

        avg.ShouldBe(20.0);
    }

    [TestMethod]
    public void CalculateAveragePrice_EmptyData_ReturnsNull()
    {
        double? avg = PriceAnalysis.CalculateAveragePrice([], s => s.ImportPrice);

        avg.ShouldBeNull();
    }

    // Edge-of-data: no false positives (Cozy/3-rate tariff scenario)
    // ========================================================================

    [TestMethod]
    public void FindTroughs_EdgePlateau_NotDetectedAsTrough()
    {
        // Tariff: standard 27.5 → cheap 16.5 → standard 27.5 → peak 38.5 → standard 27.5
        // The final 27.5 plateau is at the end of data — should NOT be a trough
        List<PricingSlot> slots = BuildCozyTariffSlots();

        List<PricingSlot> troughs = PriceAnalysis.FindTroughs(slots, s => s.ImportPrice);

        troughs.Count.ShouldBe(1);
        // Only the genuine 16.5p trough (midpoint of 2:00-5:00)
        troughs[0].ImportPrice.ShouldBe(16.5);
    }

    [TestMethod]
    public void FindPeaks_EdgePlateau_NotDetectedAsPeak()
    {
        // The initial 27.5p plateau (higher than next 16.5p) should NOT be a peak
        List<PricingSlot> slots = BuildCozyTariffSlots();

        List<PricingSlot> peaks = PriceAnalysis.FindPeaks(slots, s => s.ImportPrice);

        peaks.Count.ShouldBe(1);
        // Only the genuine 38.5p peak (midpoint of 16:00-19:00)
        peaks[0].ImportPrice.ShouldBe(38.5);
    }

    [TestMethod]
    public void FindMinimaRegionBoundaries_CozyTariff_OnlyDetectsGenuineCheapPeriod()
    {
        // The 19:00-0:00 period at 27.5p follows the 38.5p peak but should NOT
        // be detected as a minima — it's a return to standard rate, not a dip.
        List<PricingSlot> slots = BuildCozyTariffSlots();

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.Count.ShouldBe(2); // one start + one end
        PriceBoundary start = boundaries.First(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.First(b => b.BoundaryType == "end");

        start.TimeMinutes.ShouldBe(120);  // 02:00
        end.TimeMinutes.ShouldBe(300);    // 05:00
    }

    [TestMethod]
    public void FindMaximaRegionBoundaries_CozyTariff_OnlyDetectsGenuinePeak()
    {
        // The 0:00-2:00 period at 27.5p is higher than the 16.5p that follows but
        // should NOT be detected as a maxima — it's standard rate, not a spike.
        List<PricingSlot> slots = BuildCozyTariffSlots();

        List<PriceBoundary> boundaries = PriceAnalysis.FindMaximaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.Count.ShouldBe(2); // one start + one end
        PriceBoundary start = boundaries.First(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.First(b => b.BoundaryType == "end");

        start.TimeMinutes.ShouldBe(960);   // 16:00
        end.TimeMinutes.ShouldBe(1140);    // 19:00
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    /// <summary>
    /// Cozy/3-rate tariff: 0:00-2:00 27.5p, 2:00-5:00 16.5p, 5:00-16:00 27.5p, 16:00-19:00 38.5p, 19:00-0:00 27.5p
    /// </summary>
    private static List<PricingSlot> BuildCozyTariffSlots()
    {
        List<PricingSlot> slots = [];
        for (int minutes = 0; minutes < 1440; minutes += 30)
        {
            double importPrice = minutes switch
            {
                >= 0 and < 120 => 27.5,      // 00:00-02:00 standard
                >= 120 and < 300 => 16.5,     // 02:00-05:00 cheap
                >= 300 and < 960 => 27.5,     // 05:00-16:00 standard
                >= 960 and < 1140 => 38.5,    // 16:00-19:00 peak
                _ => 27.5                      // 19:00-00:00 standard
            };
            slots.Add(new PricingSlot { TimeMinutes = minutes, ImportPrice = importPrice, ExportPrice = 15 });
        }
        return slots;
    }

    // ========================================================================
    // Cross-midnight detection with extended data
    // ========================================================================

    [TestMethod]
    public void FindMinimaRegionBoundaries_ExtendedCrossMidnightCheapRate_DetectsMinima()
    {
        // Tariff: 23:30-05:30 at 7p, 05:30-23:30 at 29.9p
        // Today's slots extended with tomorrow's first 2 slots shifted +1440
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 330, ImportPrice = 29.9, ExportPrice = 15 },
            new() { TimeMinutes = 1410, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 1440, ImportPrice = 7, ExportPrice = 5 },
            new() { TimeMinutes = 1770, ImportPrice = 29.9, ExportPrice = 15 }
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        // Should detect 2 minima regions:
        // 1) 0-330 (start-of-day cheap period, has prevVal from extended yesterday if present, but here prevVal is null → not detected)
        //    Actually: slot 0 has no prev → not detected. Slot 1410 has prev=29.9, next=29.9 (via 1440 same value, then 1770 different)
        // Let's check: the plateau at 1410-1440 (both 7p) has prev=29.9 and next=29.9 → detected as minima
        // Region: starts at 1410, ends at 1770
        boundaries.Count.ShouldBeGreaterThanOrEqualTo(2);
        PriceBoundary start = boundaries.Last(b => b.BoundaryType == "start");
        PriceBoundary end = boundaries.Last(b => b.BoundaryType == "end");
        start.TimeMinutes.ShouldBe(1410);
        end.TimeMinutes.ShouldBe(1770);
    }

    // ========================================================================
    // Realistic day pattern
    // ========================================================================

    [TestMethod]
    public void RealisticFluxDay_FindsCheapAndExpensiveRegions()
    {
        // Simulate Flux: cheap 02:00-05:00, peak 16:00-19:00, standard elsewhere
        List<PricingSlot> slots = [];
        for (int minutes = 0; minutes < 1440; minutes += 30)
        {
            double price = minutes switch
            {
                >= 120 and < 300 => 7,
                >= 960 and < 1140 => 34,
                _ => 20
            };
            slots.Add(new PricingSlot { TimeMinutes = minutes, ImportPrice = price, ExportPrice = 15 });
        }

        List<PriceBoundary> minima = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);
        List<PriceBoundary> maxima = PriceAnalysis.FindMaximaRegionBoundaries(slots, s => s.ImportPrice);

        // Cheap region
        minima.Count.ShouldBe(2);
        PriceBoundary cheapStart = minima.First(b => b.BoundaryType == "start");
        PriceBoundary cheapEnd = minima.First(b => b.BoundaryType == "end");
        cheapStart.TimeMinutes.ShouldBe(120); // 02:00
        cheapEnd.TimeMinutes.ShouldBe(300);   // 05:00

        // Expensive region
        maxima.Count.ShouldBe(2);
        PriceBoundary expensiveStart = maxima.First(b => b.BoundaryType == "start");
        PriceBoundary expensiveEnd = maxima.First(b => b.BoundaryType == "end");
        expensiveStart.TimeMinutes.ShouldBe(960);  // 16:00
        expensiveEnd.TimeMinutes.ShouldBe(1140);   // 19:00
    }
}
