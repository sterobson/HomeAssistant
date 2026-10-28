using HomeAssistant.Services.Climate;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// A shower is the bathroom's humidity up <see cref="ShowerDetector.HumidityRise"/> points within
/// <see cref="ShowerDetector.RiseWithin"/>, as measured for HOME-ASSISTANT.md gotcha 29. Readings are the sensor's own,
/// read from the recorder (UTC).
/// </summary>
[TestClass]
public sealed class ShowerDetectorTests
{
    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, TimeSpan.Zero);

    // 2026-10-27, the morning of gotcha 30.
    private static readonly (DateTimeOffset At, double Humidity)[] ShowerOn27October =
    [
        (At(27, 6, 31, 1), 84.4),
        (At(27, 6, 36, 59), 83.4),
        (At(27, 6, 43, 7), 82.4),
        (At(27, 6, 50, 16), 83.9),
        (At(27, 6, 50, 26), 85.2),
        (At(27, 6, 50, 36), 86.3),
        (At(27, 6, 50, 46), 87.6),
        (At(27, 6, 50, 56), 88.6),
        (At(27, 6, 52, 27), 89.7),
        (At(27, 6, 53, 38), 91.0),
        (At(27, 6, 57, 10), 92.0),
        (At(27, 6, 57, 35), 93.3),
        (At(27, 7, 13, 18), 91.4),
        (At(27, 7, 41, 38), 90.4),
    ];

    [TestMethod]
    public void AShower_IsFoundFromWhenTheHumidityBeganToRise()
    {
        ShowerDetector.LatestShowerStart(ShowerOn27October, At(27, 7, 9, 32)).ShouldBe(At(27, 6, 50, 16));
    }

    [TestMethod]
    public void AShower_NoLongerCounts_OnceItCannotBeWarmingTheRadiators()
    {
        DateTimeOffset now = At(27, 6, 50, 16) + ShowerDetector.WarmsRadiatorsFor + TimeSpan.FromMinutes(1);

        ShowerDetector.LatestShowerStart(ShowerOn27October, now).ShouldBeNull();
    }

    [TestMethod]
    public void NoShower_WhenTheHumidityBarelyMoves()
    {
        // 2026-10-28, 12:07-13:07: the radiators climbed with nobody showering; the boiler was running.
        (DateTimeOffset At, double Humidity)[] flat = [(At(28, 12, 7, 6), 74.0), (At(28, 12, 32, 9), 75.0), (At(28, 13, 7, 27), 74.0)];

        ShowerDetector.LatestShowerStart(flat, At(28, 13, 10)).ShouldBeNull();
    }

    [TestMethod]
    public void NoShower_WhenTheHumidityCreepsUpSlowly()
    {
        // Eight points, but over two hours: weather, not a shower.
        (DateTimeOffset At, double Humidity)[] creeping = [.. Enumerable.Range(0, 9).Select(i => (At(28, 8, 0) + TimeSpan.FromMinutes(15 * i), 60.0 + i))];

        ShowerDetector.LatestShowerStart(creeping, At(28, 10, 5)).ShouldBeNull();
    }
}
