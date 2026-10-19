using HomeAssistant.Services.Climate;
using HomeAssistant.Shared.Climate;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The per-device rule logic on its own: time windows (days, end exclusive, past midnight) and presence
/// (Any / All). 2026-10-19 is a Monday.
/// </summary>
[TestClass]
public sealed class DeviceRulesEvaluatorTests
{
    private static readonly Func<string, bool> NobodyIn = _ => false;

    private static DateTime At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0);

    private static TimeWindowDto Window(string start, string end, Days days = Days.Everyday) => new() { Start = start, End = end, Days = days };

    [TestMethod]
    public void NoRules_AllowAnything()
    {
        DeviceRulesEvaluator.Allows(null, At(19, 3, 0), NobodyIn).ShouldBeTrue();
        DeviceRulesEvaluator.Allows(new DeviceRulesDto(), At(19, 3, 0), NobodyIn).ShouldBeTrue("no windows and no presence means no restriction");
    }

    [TestMethod]
    [DataRow(5, 29, false, DisplayName = "just before the start")]
    [DataRow(5, 30, true, DisplayName = "the start counts")]
    [DataRow(21, 59, true, DisplayName = "just before the end")]
    [DataRow(22, 0, false, DisplayName = "the end does not count")]
    public void Window_IsStartInclusiveEndExclusive(int hour, int minute, bool expected)
    {
        DeviceRulesEvaluator.IsWithinWindows([Window("05:30", "22:00")], At(19, hour, minute)).ShouldBe(expected);
    }

    [TestMethod]
    public void AnyOfSeveralWindows_Counts()
    {
        List<TimeWindowDto> windows = [Window("08:00", "08:30"), Window("21:00", "21:15")];

        DeviceRulesEvaluator.IsWithinWindows(windows, At(19, 8, 10)).ShouldBeTrue();
        DeviceRulesEvaluator.IsWithinWindows(windows, At(19, 21, 5)).ShouldBeTrue();
        DeviceRulesEvaluator.IsWithinWindows(windows, At(19, 12, 0)).ShouldBeFalse();
    }

    [TestMethod]
    public void Window_OnlyOnItsDays()
    {
        TimeWindowDto weekdays = Window("09:00", "17:00", Days.Weekdays);

        DeviceRulesEvaluator.IsWithinWindows([weekdays], At(19, 12, 0)).ShouldBeTrue("Monday");
        DeviceRulesEvaluator.IsWithinWindows([weekdays], At(18, 12, 0)).ShouldBeFalse("Sunday");
    }

    [TestMethod]
    public void PastMidnight_BelongsToTheDayItStarted()
    {
        // Friday 22:00 to 06:00: covers Friday night and early Saturday, but not early Friday.
        TimeWindowDto fridayNight = Window("22:00", "06:00", Days.Friday);

        DeviceRulesEvaluator.IsWithinWindows([fridayNight], At(23, 23, 0)).ShouldBeTrue("Friday 23:00");
        DeviceRulesEvaluator.IsWithinWindows([fridayNight], At(24, 3, 0)).ShouldBeTrue("Saturday 03:00 is still Friday's window");
        DeviceRulesEvaluator.IsWithinWindows([fridayNight], At(23, 3, 0)).ShouldBeFalse("Friday 03:00 would be Thursday's window");
        DeviceRulesEvaluator.IsWithinWindows([fridayNight], At(24, 6, 0)).ShouldBeFalse("the end does not count");
    }

    [TestMethod]
    public void UnreadableTime_NeverMatches()
    {
        DeviceRulesEvaluator.IsWithinWindows([Window("8am", "22:00")], At(19, 12, 0)).ShouldBeFalse();
    }

    [TestMethod]
    public void Presence_AnyOrAll()
    {
        Func<string, bool> onlyDen = source => source == "den";
        PresenceRuleDto any = new() { Match = PresenceMatch.Any, Sources = ["den", "living room"] };
        PresenceRuleDto all = new() { Match = PresenceMatch.All, Sources = ["den", "living room"] };

        DeviceRulesEvaluator.IsPresenceSatisfied(any, onlyDen).ShouldBeTrue();
        DeviceRulesEvaluator.IsPresenceSatisfied(all, onlyDen).ShouldBeFalse();
        DeviceRulesEvaluator.IsPresenceSatisfied(all, _ => true).ShouldBeTrue();
        DeviceRulesEvaluator.IsPresenceSatisfied(new PresenceRuleDto(), NobodyIn).ShouldBeTrue("no sources means no presence needed");
    }

    [TestMethod]
    public void WindowsAndPresence_MustBothPass()
    {
        DeviceRulesDto kitchen = new()
        {
            TimeWindows = [Window("05:30", "22:00")],
            Presence = new() { Sources = [PresenceSources.House] }
        };
        Func<string, bool> someoneHome = source => source == PresenceSources.House;

        DeviceRulesEvaluator.Allows(kitchen, At(19, 12, 0), someoneHome).ShouldBeTrue();
        DeviceRulesEvaluator.Allows(kitchen, At(19, 12, 0), NobodyIn).ShouldBeFalse("in the window but nobody home");
        DeviceRulesEvaluator.Allows(kitchen, At(19, 23, 0), someoneHome).ShouldBeFalse("someone home but outside the window");
    }
}
