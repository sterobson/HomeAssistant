// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names

using HomeAssistant.apps;
using HomeAssistantGenerated;
using System.Threading.Tasks;

namespace HassModel;

/// <summary>
///     Hello world showcase using the new HassModel API
/// </summary>
[NetDaemonApp]
public class Wooooooo
{
    public Wooooooo(IHaContext ha, ITriggerManager triggerManager)
    {
        Entities entities = new(ha);
        MyDevices myDevices = new(entities, ha);

        myDevices.PorchMotionSensor.StateChanges().SubscribeAsync(async e =>
        {
            if (e.New?.State == "on")
            {
                // Halloween sounds
                DateTime now = DateTime.Now;
                if (now.Month == 10 && now.Day == 31 && now.Hour >= 16 && now.Hour <= 21)
                {
                    _ = PlaySounds(myDevices);
                    _ = FlashLight(myDevices.PorchLight);
                }
            }
        });
    }

    private static async Task PlaySounds(MyDevices myDevices)
    {
        myDevices.GamesRoomSpeaker.PlayMedia(new MediaPlayerPlayMediaParameters
        {
            Media = new
            {
                media_content_id = "http://192.168.1.188:8123/media/local/thunderstorm.mp3",
                media_content_type = "music"
            }
        });

        await Task.Delay(TimeSpan.FromSeconds(8));

        myDevices.GamesRoomSpeaker.PlayMedia(new MediaPlayerPlayMediaParameters
        {
            Media = new
            {
                media_content_id = "http://192.168.1.188:8123/media/local/wolf.mp3",
                media_content_type = "music"
            }
        });
    }

    private async Task FlashLight(LightEntity light)
    {

        System.Collections.Generic.IReadOnlyList<double>? colour = light.Attributes?.RgbColor;
        string? effect = light.Attributes?.Effect;
        double? currentBrightness = light.Attributes?.Brightness;

        int turnOnAfter = 3;
        int i = 0;
        while (colour == null && effect == null && currentBrightness == null)
        {
            if (++i == turnOnAfter)
            {
                light.TurnOn();
            }
            else if (i > turnOnAfter)
            {
                return;
            }

            if (i > 0)
            {
                await Task.Delay(1000);
            }

            colour = light.Attributes?.RgbColor;
            effect = light.Attributes?.Effect;
            currentBrightness = light.Attributes?.Brightness;
        }

        await ToNewColour(light, 255, 85, 0, 50, TimeSpan.FromSeconds(1));

        await ToNewColour(light, 255, 85, 0, 100, TimeSpan.FromSeconds(0.5));
        await ToNewColour(light, 10, 255, 0, 100, TimeSpan.FromSeconds(0.5));
        await ToNewColour(light, 255, 0, 155, 50, TimeSpan.FromSeconds(0.5));
        await ToNewColour(light, 255, 85, 0, 80, TimeSpan.FromSeconds(5));

        await ToNewColour(light, 255, 85, 0, 10, TimeSpan.FromSeconds(3));
        await ToNewColour(light, 255, 85, 0, 100, TimeSpan.FromSeconds(3));

        await ToNewColour(light, 255, 85, 0, 10, TimeSpan.FromSeconds(3));
        await ToNewColour(light, 255, 85, 0, 100, TimeSpan.FromSeconds(3));

        await ToNewColour(light, 255, 85, 0, 10, TimeSpan.FromSeconds(3));
        await ToNewColour(light, 255, 85, 0, 100, TimeSpan.FromSeconds(3));

        if (colour == null && effect != null)
        {
            light.TurnOn(effect: effect);
        }
        else if (colour != null)
        {
            await ToNewColour(light, colour[0], colour[1], colour[2], (currentBrightness ?? 100) * 100 / 255, TimeSpan.FromSeconds(3));
        }
    }

    private static async Task ToNewColour(LightEntity light, double r, double g, double b, double brightnessPercent, TimeSpan timeToTake)
    {
        System.Collections.Generic.IReadOnlyList<double>? colour = light.Attributes?.RgbColor;
        double currentBrightness = (100 * light.Attributes?.Brightness / 255) ?? 0;

        double initialR = colour?[0] ?? 255;
        double initialG = colour?[1] ?? 255;
        double initialB = colour?[2] ?? 255;

        // 5ms increments
        int stepMs = 100;
        int totalSteps = (int)Math.Floor(timeToTake.TotalMilliseconds / stepMs);

        double dr = (r - initialR) / totalSteps;
        double dg = (g - initialG) / totalSteps;
        double db = (b - initialB) / totalSteps;
        double dbr = (brightnessPercent - currentBrightness) / totalSteps;

        int step = 1;
        while (step <= totalSteps)
        {
            int newR = (int)(initialR + (step * dr));
            int newG = (int)(initialG + (step * dg));
            int newB = (int)(initialB + (step * db));
            int newBright = (int)(currentBrightness + (step * dbr));
            light.SetRgb(newR, newG, newB, newBright);

            await Task.Delay(TimeSpan.FromMilliseconds(stepMs));
            step++;
        }

    }

}