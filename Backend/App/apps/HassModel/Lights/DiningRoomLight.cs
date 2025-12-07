using HomeAssistantGenerated;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Lights;


[NetDaemonApp]
internal class DiningRoomLight
{
    public DiningRoomLight(IHaContext ha, NamedEntities namedEntities, IScheduler scheduler)
    {
        Entities entities = new(ha);

        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), () =>
        {
            ProcessPeriodicCheck(namedEntities);
        });

        SonoffButtonGroup buttonGroup = new(ha, namedEntities.DiningRoomBookshelfButton, namedEntities.LivingRoomChristmasTreeButton);

        // Single press to turn on or off.
        buttonGroup.SinglePressed().SubscribeAsync(async e =>
        {
            await ManuallyToggleLights(namedEntities);
        });

        // Double press to cycle the colours.
        buttonGroup.DoublePressed().SubscribeAsync(async e =>
        {
            await CycleLightStripColour(namedEntities);
        });

        // Long press to cycle the brightness
        buttonGroup.LongPressed().SubscribeAsync(async e =>
        {
            await CycleLightStripBrightness(namedEntities);
        });

        // Turn the Lego village off if there's no motion detected.
        namedEntities.KitchenMotionSensor.StateChanges().SubscribeAsync(async e =>
        {
            await ProcessMotionDetected(namedEntities, e);
        });

        // When the Christmas tree is turned on or off, ensure that the bookshelf does the same.
        namedEntities.LivingRoomChristmasTreePlugOnOff.SubscribeToStateChangesAsync(async e =>
        {
            await SynchronisePlugStates(e,
                namedEntities.DiningBookshelfLightStripPlugOnOff,
                namedEntities.DiningRoomLegoVillage
                );
        });

        // When the bookshelf is turned on or off, ensure that the Christmas tree does the same.
        namedEntities.DiningBookshelfLightStripPlugOnOff.SubscribeToStateChangesAsync(async e =>
        {
            await SynchronisePlugStates(e,
                namedEntities.LivingRoomChristmasTreePlugOnOff,
                namedEntities.DiningRoomLegoVillage);
        });
    }

    private static async Task SynchronisePlugStates(ICustomSwitchEntity primary, params ICustomSwitchEntity[] secondaries)
    {
        bool primaryIsOn = primary.IsOn();
        foreach (ICustomSwitchEntity secondary in secondaries)
        {
            if (primaryIsOn && !secondary.IsOn())
            {
                secondary.TurnOn();
            }
            else if (!primaryIsOn && !secondary.IsOff())
            {
                secondary.TurnOff();
            }
        }
    }

    private static async Task ProcessMotionDetected(NamedEntities namedEntities, NetDaemon.HassModel.Entities.StateChange<BinarySensorEntity, NetDaemon.HassModel.Entities.EntityState<BinarySensorAttributes>> e)
    {
        if (!namedEntities.DiningBookshelfLightStripPlugOnOff.IsOn())
        {
            // Lights are already off.
            return;
        }

        if (e.New?.State == "on")
        {
            namedEntities.DiningRoomLegoVillage.TurnOn();
        }
        else
        {
            namedEntities.DiningRoomLegoVillage.TurnOff();
        }

        return;
    }

    private static async Task CycleLightStripBrightness(NamedEntities namedEntities)
    {
        if (namedEntities.DiningBookshelfLightStrip?.Attributes == null)
        {
            return;
        }

        if (!namedEntities.DiningBookshelfLightStripPlugOnOff.IsOn())
        {
            namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOn();
        }

        int newBrightnessPct = namedEntities.DiningBookshelfLightStrip.Attributes.Brightness switch
        {
            >= 128 => 10,
            _ => 100
        };

        namedEntities.DiningBookshelfLightStrip.SetBrightnessPercent(newBrightnessPct);
    }

    private static async Task CycleLightStripColour(NamedEntities namedEntities)
    {
        if (!namedEntities.DiningBookshelfLightStripPlugOnOff.IsOn())
        {
            namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOn();
        }

        // Get the named colour
        LightEntityExtensions.FavouriteColour currentColour = namedEntities.DiningBookshelfLightStrip.GetFavouriteColour();

        LightEntityExtensions.FavouriteColour newColour = currentColour switch
        {
            LightEntityExtensions.FavouriteColour.Purple => LightEntityExtensions.FavouriteColour.Blue,
            LightEntityExtensions.FavouriteColour.Blue => LightEntityExtensions.FavouriteColour.Red,
            LightEntityExtensions.FavouriteColour.Red => LightEntityExtensions.FavouriteColour.White,
            _ => LightEntityExtensions.FavouriteColour.Purple
        };

        namedEntities.DiningBookshelfLightStrip.SetColour(newColour);
    }

    private static async Task ManuallyToggleLights(NamedEntities namedEntities)
    {
        if (namedEntities.DiningBookshelfLightStripPlugOnOff.IsOn())
        {
            namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOff();
            namedEntities.DiningRoomLegoVillage.TurnOff();
        }
        else
        {
            namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOn();
            namedEntities.DiningRoomLegoVillage.TurnOn();
        }
    }

    private static bool ProcessPeriodicCheck(NamedEntities namedEntities)
    {
        // If after 10pm and before 6am and the light is on and the desks are not on, then turn off
        if (DateTime.Now.Hour < 22 && DateTime.Now.Hour >= 6)
        {
            return false;
        }

        if (namedEntities.DiningBookshelfLightStripPlugOnOff.IsOff())
        {
            // The plug's already off, so no action.
            return false;
        }

        if (namedEntities.GamesRoomDeskPlugOnOff.IsOn() || namedEntities.DiningRoomDeskPlugOnOff.IsOn())
        {
            // One of the desks is on, someone's still around.
            return false;
        }

        namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOff();
        namedEntities.DiningRoomLegoVillage.TurnOff();
        return true;
    }
}