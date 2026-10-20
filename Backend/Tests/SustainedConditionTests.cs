using HomeAssistant.Services;
using Shouldly;

namespace HomeAssistant.Tests;

[TestClass]
public sealed class SustainedConditionTests
{
    private static readonly TimeSpan Five = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void IsHeldOnlyOnceTheConditionHasBeenTrueForTheWholeDuration()
    {
        bool holds = false;
        FakeTimeProvider clock = NewClock();
        SustainedCondition sustained = new(() => holds, clock);

        holds = true;
        sustained.Observe();
        sustained.HeldFor(Five).ShouldBeFalse();

        Advance(clock, 4.9);
        sustained.HeldFor(Five).ShouldBeFalse();

        Advance(clock, 0.1);
        sustained.HeldFor(Five).ShouldBeTrue("exactly the duration counts");
    }

    [TestMethod]
    public void ABreakRestartsTheCount()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        SustainedCondition sustained = new(() => holds, clock);
        Advance(clock, 4);

        holds = false;
        sustained.Observe();
        Advance(clock, 1);
        holds = true;
        sustained.Observe();
        Advance(clock, 4);

        sustained.HeldFor(Five).ShouldBeFalse("only 4 s since the break");
        Advance(clock, 1);
        sustained.HeldFor(Five).ShouldBeTrue();
    }

    [TestMethod]
    public void ReReadsTheConditionWhenAsked_SoItNeverDependsOnANotificationHavingBeenHandled()
    {
        bool holds = true;
        FakeTimeProvider clock = NewClock();
        SustainedCondition sustained = new(() => holds, clock);
        Advance(clock, 10);
        sustained.HeldFor(Five).ShouldBeTrue();

        holds = false;

        sustained.HeldFor(Five).ShouldBeFalse("no Observe call, but the condition is read again");
    }

    [TestMethod]
    public void ACondition_ThatCannotBeRead_IsNotHolding()
    {
        bool broken = false;
        FakeTimeProvider clock = NewClock();
        SustainedCondition sustained = new(() => broken ? throw new InvalidOperationException("entity unavailable") : true, clock);
        Advance(clock, 10);
        sustained.HeldFor(Five).ShouldBeTrue();

        broken = true;

        sustained.HeldFor(Five).ShouldBeFalse();
    }

    [TestMethod]
    public void AConditionAlreadyTrueAtStart_IsCountedFromStart()
    {
        FakeTimeProvider clock = NewClock();
        SustainedCondition sustained = new(() => true, clock);

        sustained.HeldFor(Five).ShouldBeFalse("conservative until it has been watched for the duration");
        Advance(clock, 5);
        sustained.HeldFor(Five).ShouldBeTrue();
    }

    private static FakeTimeProvider NewClock()
    {
        FakeTimeProvider clock = new();
        clock.SetSpecificDateTime(new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero));
        return clock;
    }

    private static void Advance(FakeTimeProvider clock, double seconds) =>
        clock.SetSpecificDateTime(clock.GetUtcNow() + TimeSpan.FromSeconds(seconds));
}
