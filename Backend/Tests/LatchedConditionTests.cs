using HomeAssistant.Services;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class LatchedConditionTests
{
    private static readonly TimeSpan Five = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Fifteen = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void ComesOnOnlyOnceTheConditionHasBeenTrueForTheWholeOnDuration()
    {
        bool holds = false;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);

        holds = true;
        latch.Observe();
        Advance(clock, 4.9);
        latch.IsOn.ShouldBeFalse();

        Advance(clock, 0.1);
        latch.IsOn.ShouldBeTrue("exactly the duration counts");
    }

    [TestMethod]
    public void BeforeItIsOn_ABreakRestartsTheCount()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);
        Advance(clock, 4);

        holds = false;
        latch.Observe();
        Advance(clock, 1);
        holds = true;
        latch.Observe();
        Advance(clock, 4);

        latch.IsOn.ShouldBeFalse("only 4 s since the break");
        Advance(clock, 1);
        latch.IsOn.ShouldBeTrue();
    }

    [TestMethod]
    public void OnceOn_StaysOnUntilTheConditionHasBeenFalseForTheWholeOffDuration()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);
        Advance(clock, 10);
        latch.IsOn.ShouldBeTrue();

        holds = false;
        latch.Observe();
        Advance(clock, 14.9);
        latch.IsOn.ShouldBeTrue("the hold");

        Advance(clock, 0.1);
        latch.IsOn.ShouldBeFalse();
    }

    [TestMethod]
    public void OnceOn_ABreakShorterThanTheHoldIsBridged_AndTheNextOneIsTimedAfresh()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);
        Advance(clock, 10);

        holds = false;
        latch.Observe();
        Advance(clock, 10);
        holds = true;
        latch.Observe();
        Advance(clock, 1);
        latch.IsOn.ShouldBeTrue("a 10 s break is bridged, with no fresh 5 s needed");

        holds = false;
        latch.Observe();
        Advance(clock, 10);
        latch.IsOn.ShouldBeTrue("this break has only lasted 10 s, not 21");
    }

    [TestMethod]
    public void OnceOff_ItNeedsTheWholeOnDurationAgain()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);
        Advance(clock, 10);
        holds = false;
        latch.Observe();
        Advance(clock, 20);

        holds = true;
        latch.Observe();
        Advance(clock, 4);

        latch.IsOn.ShouldBeFalse();
        Advance(clock, 1);
        latch.IsOn.ShouldBeTrue();
    }

    [TestMethod]
    public void ARunLongEnoughCounts_EvenIfNobodyAskedDuringIt()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => holds, Five, Fifteen, clock);
        Advance(clock, 6);

        holds = false;
        latch.Observe();
        Advance(clock, 2);

        latch.IsOn.ShouldBeTrue("it came on at 5 s, before the break");
    }

    [TestMethod]
    public void ACondition_ThatCannotBeRead_IsABreak()
    {
        bool broken = false;
        FakeTimeProvider clock = NewClock();
        LatchedCondition latch = new(() => broken ? throw new InvalidOperationException("entity unavailable") : true, Five, Fifteen, clock);
        Advance(clock, 10);
        latch.IsOn.ShouldBeTrue();

        broken = true;
        latch.Observe();
        Advance(clock, 15);

        latch.IsOn.ShouldBeFalse();
    }

    private static FakeTimeProvider NewClock()
    {
        FakeTimeProvider clock = new();
        clock.SetSpecificDateTime(new DateTimeOffset(2026, 10, 22, 13, 44, 0, TimeSpan.Zero));
        return clock;
    }

    private static void Advance(FakeTimeProvider clock, double seconds) =>
        clock.SetSpecificDateTime(clock.GetUtcNow() + TimeSpan.FromSeconds(seconds));
}
