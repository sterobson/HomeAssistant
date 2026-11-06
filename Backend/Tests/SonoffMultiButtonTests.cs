using HomeAssistant.apps;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;
using NSubstitute;
using Shouldly;
using System.Reactive.Subjects;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// The SNZB-01M multi button reports single, double, triple and hold, with the button as the event's endpoint
/// (HOME-ASSISTANT.md gotcha 33). It also sends level-control step and stop events on endpoint 1, which must
/// never be mistaken for a press.
/// </summary>
[TestClass]
public sealed class SonoffMultiButtonTests
{
    private const string Ieee = "a4:c1:38:10:a1:af:ff:ff";

    private static Event ZhaEvent(string ieee, int endpoint, string command)
    {
        JsonElement data = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["device_ieee"] = ieee,
            ["endpoint_id"] = endpoint,
            ["command"] = command,
        });
        return new Event { EventType = "zha_event", DataElement = data };
    }

    private static (Subject<Event> Events, IHaContext Ha) CreateContext()
    {
        Subject<Event> events = new();
        IHaContext ha = Substitute.For<IHaContext>();
        ha.Events.Returns(events);
        return (events, ha);
    }

    [TestMethod]
    public void EachClickType_FiresOnlyItsOwnObservable()
    {
        (Subject<Event> events, IHaContext ha) = CreateContext();
        SonoffMultiButton button = new(ha, Ieee, 2);
        int doubles = 0;
        int triples = 0;
        int longs = 0;
        button.DoublePressed().Subscribe(_ => doubles++);
        button.TriplePressed().Subscribe(_ => triples++);
        button.LongPressed().Subscribe(_ => longs++);

        events.OnNext(ZhaEvent(Ieee, 2, "double"));
        events.OnNext(ZhaEvent(Ieee, 2, "triple"));
        events.OnNext(ZhaEvent(Ieee, 2, "triple"));
        events.OnNext(ZhaEvent(Ieee, 2, "hold"));
        events.OnNext(ZhaEvent(Ieee, 2, "hold"));
        events.OnNext(ZhaEvent(Ieee, 2, "hold"));

        doubles.ShouldBe(1);
        triples.ShouldBe(2);
        longs.ShouldBe(3);
    }

    [TestMethod]
    public void AButton_IgnoresTheOtherButtonsOnTheSameRemote()
    {
        (Subject<Event> events, IHaContext ha) = CreateContext();
        SonoffMultiButton buttonThree = new(ha, Ieee, 3);
        int presses = 0;
        buttonThree.DoublePressed().Subscribe(_ => presses++);

        events.OnNext(ZhaEvent(Ieee, 1, "double"));
        events.OnNext(ZhaEvent(Ieee, 2, "double"));
        events.OnNext(ZhaEvent(Ieee, 4, "double"));
        presses.ShouldBe(0);

        events.OnNext(ZhaEvent(Ieee, 3, "double"));
        presses.ShouldBe(1);
    }

    [TestMethod]
    public void AButton_IgnoresOtherDevices_AndMatchesTheAddressInAnyCase()
    {
        (Subject<Event> events, IHaContext ha) = CreateContext();
        SonoffMultiButton button = new(ha, Ieee.ToUpperInvariant(), 1);
        int presses = 0;
        button.TriplePressed().Subscribe(_ => presses++);

        events.OnNext(ZhaEvent("d4:48:67:ff:fe:0b:f6:0b", 1, "triple"));
        presses.ShouldBe(0);

        events.OnNext(ZhaEvent(Ieee, 1, "triple"));
        presses.ShouldBe(1);
    }

    [TestMethod]
    public void TheLevelControlEventsOnEndpointOne_AreNotPresses()
    {
        (Subject<Event> events, IHaContext ha) = CreateContext();
        SonoffMultiButton button = new(ha, Ieee, 1);
        int presses = 0;
        button.DoublePressed().Subscribe(_ => presses++);
        button.TriplePressed().Subscribe(_ => presses++);
        button.LongPressed().Subscribe(_ => presses++);

        events.OnNext(ZhaEvent(Ieee, 1, "step"));
        events.OnNext(ZhaEvent(Ieee, 1, "stop"));
        events.OnNext(ZhaEvent(Ieee, 1, "single"));

        presses.ShouldBe(0);
    }
}
