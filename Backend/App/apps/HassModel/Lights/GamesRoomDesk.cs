using HomeAssistantGenerated;
using NetDaemon.HassModel.Entities;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Lights;


[NetDaemonApp]
internal class GamesRoomDesk
{
    private bool _isShutdownRunning = false;
    private bool _cancel = false;

    public GamesRoomDesk(IHaContext ha, NamedEntities namedEntities, ILogger<GamesRoomDesk> logger)
    {
        Entities entities = new(ha);

        // Single press to turn on or off.
        namedEntities.GamesRoomDeskButton.SinglePressed().SubscribeAsync(async e =>
        {
            if (_isShutdownRunning)
            {
                _cancel = true;
            }
            else if (!namedEntities.GamesRoomDeskPlugOnOff.IsOn())
            {
                namedEntities.GamesRoomDeskPlugOnOff.TurnOn();
                logger.LogDebug("Turned on {DeviceName}", nameof(namedEntities.GamesRoomDeskPlugOnOff));
            }
            else if (!namedEntities.GamesRoomDeskPlugOnOff.IsOff())
            {
                _ = Task.Run(async () => await FlashThenTurnOff(namedEntities, logger));
            }
        });
    }

    private async Task FlashThenTurnOff(NamedEntities namedEntities, ILogger logger)
    {
        try
        {
            _isShutdownRunning = true;
            if (namedEntities.GamesRoomDeskLamp.IsOn())
            {
                long brightnessPct = (long)(100 * (namedEntities.GamesRoomDeskLamp.Attributes?.Brightness ?? 0) / 255);
                if (brightnessPct > 0)
                {
                    for (int i = 0; i <= 2; i++)
                    {
                        namedEntities.GamesRoomDeskLamp.SetBrightnessPercent(10);
                        await Task.Delay(TimeSpan.FromSeconds(0.25));
                        namedEntities.GamesRoomDeskLamp.SetBrightnessPercent(brightnessPct);
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        if (_cancel)
                        {
                            // Never shutdown if cancelled.
                            return;
                        }
                    }
                }
            }

            namedEntities.GamesRoomDeskPlugOnOff.TurnOff();
            logger.LogDebug("Turned off {DeviceName}", nameof(namedEntities.GamesRoomDeskPlugOnOff));
        }
        finally
        {
            _isShutdownRunning = false;
            _cancel = false;
        }
    }
}
