using HomeAssistantGenerated;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Car;


[NetDaemonApp]
internal class CarAutomations
{
    private readonly IHaContext _ha;
    private readonly ILogger<CarAutomations> _logger;

    public CarAutomations(IHaContext ha, ITriggerManager triggerManager, ILogger<CarAutomations> logger)
    {
        Entities entities = new(ha);
        MyDevices myDevices = new(entities, ha);

        myDevices.HallwayButton.Pressed().SubscribeAsync(async e =>
        {
            _ = SetCarClimate(entities, myDevices);
        });

        myDevices.Car.IgnitionEntity.StateChanges().SubscribeAsync(async e =>
        {
            _ = WarnAboutOpenWindows(entities);
        });
        _ha = ha;
        _logger = logger;
    }

    private async Task SetCarClimate(Entities entities, MyDevices myDevices)
    {
        _logger.LogInformation("{Method} called", nameof(SetCarClimate));

        // Note: we should avoid toggling the buttons too often, because otherwise the car will get
        // annoyed at us and stop performing the actions. Only toggle if it's a genuine change.
        const double targetTemperature = 20;
        const int checkSeconds = 60;
        int maxIterations = 10;
        int i = 0;
        CancellationTokenSource cts = new();

        myDevices.Car.IgnitionEntity.StateChanges().SubscribeAsync(async e =>
        {
            // If the car's on/off status changes at all, then shut this all down.
            await cts.CancelAsync();
        });

        bool isFanOn = false;

        while (i++ < maxIterations)
        {
            double weatherTemperature = entities.Weather.ForecastHome.EntityState?.Attributes?.Temperature ?? targetTemperature;
            double externalTemperature = entities.Sensor.MgMg4ElectricExteriorTemperature.State ?? weatherTemperature;
            double internalTemperature = entities.Sensor.MgMg4ElectricInteriorTemperature.State ?? externalTemperature;
            double minTemperature = Math.Min(weatherTemperature, Math.Min(externalTemperature, internalTemperature));

            if (internalTemperature < targetTemperature)
            {
                // Turn on heating
                if (!isFanOn)
                {
                    entities.Climate.MgMg4ElectricClimate.SetFanMode(new ClimateSetFanModeParameters { FanMode = "high" });
                    entities.Climate.MgMg4ElectricClimate.SetTemperature(new ClimateSetTemperatureParameters { Temperature = targetTemperature });
                    entities.Climate.MgMg4ElectricClimate.TurnOn();
                    isFanOn = true;
                }

                myDevices.Car.SetFrontDefrost(minTemperature < 4);
                myDevices.Car.SetRearDefrost(minTemperature < 2);
            }
            else
            {
                break;
            }

            if (i < maxIterations)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(checkSeconds), cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        // Make sure everything is switched off.
        if (isFanOn)
        {
            entities.Climate.MgMg4ElectricClimate.TurnOff();
        }

        myDevices.Car.SetFrontDefrost(false);
        myDevices.Car.SetRearDefrost(false);
    }

    private static async Task WarnAboutOpenWindows(Entities entities)
    {
        // If the car is on, or any door is open, then don't warn anout windows.
        if (entities.BinarySensor.MgMg4ElectricEngineStatus.State != "off")
        {
            return;
        }

    }

}
