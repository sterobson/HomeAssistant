using HomeAssistantGenerated;
using System.Reactive.Concurrency;

namespace HomeAssistant.apps.HassModel.Lights;


[NetDaemonApp]
internal class DiningRoomLight
{
    public DiningRoomLight(IHaContext ha, NamedEntities namedEntities, IScheduler scheduler)
    {
        Entities entities = new(ha);

        // Single press tpo turn on or off.
        namedEntities.DiningRoomBookshelfButton.SinglePressed().SubscribeAsync(async e =>
        {
            if (namedEntities.DiningBookshelfLightStripPlugOnOff.IsOn())
            {
                namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOff();
            }
            else
            {
                namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOn();
            }
        });

        // Double press to cycle the colours.
        namedEntities.DiningRoomBookshelfButton.DoublePressed().SubscribeAsync(async e =>
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
        });

        // Long press to cycle the brightness
        namedEntities.DiningRoomBookshelfButton.LongPressed().SubscribeAsync(async e =>
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
        });

        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), () =>
        {
            // If after 10pm and before 6am and the light is on and the desks are not on, then turn off
            if (DateTime.Now.Hour < 22 && DateTime.Now.Hour >= 6)
            {
                return;
            }

            if (namedEntities.DiningBookshelfLightStripPlugOnOff.IsOff())
            {
                // The plug's already off, so no action.
                return;
            }

            if (namedEntities.GamesRoomDeskPlugOnOff.IsOn() || namedEntities.DiningRoomDeskPlugOnOff.IsOn())
            {
                // One of the desks is on, someone's still around.
                return;
            }

            namedEntities.DiningBookshelfLightStripPlugOnOff.TurnOff();
        });
    }
}
