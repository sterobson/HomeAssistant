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
    public void FindMinimaRegionBoundaries_MinimaAtEdge_NotDetected()
    {
        // A minimum at the very start or end doesn't have both neighbours
        List<PricingSlot> slots =
        [
            new() { TimeMinutes = 0, ImportPrice = 5 },   // low at start - no prev
            new() { TimeMinutes = 30, ImportPrice = 20 },
            new() { TimeMinutes = 60, ImportPrice = 30 },
        ];

        List<PriceBoundary> boundaries = PriceAnalysis.FindMinimaRegionBoundaries(slots, s => s.ImportPrice);

        boundaries.ShouldBeEmpty();
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
