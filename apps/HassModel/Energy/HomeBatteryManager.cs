using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Weather;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class HomeBatteryManager
{
    private readonly IElectricityMeter _electricityMeter;
    private readonly IHomeBattery _homeBattery;
    private readonly ICarCharger _carCharger;
    private readonly NotificationService _notificationService;
    private readonly IWeatherProvider _weatherProvider;

    public HomeBatteryManager(IHaContext ha, IScheduler scheduler, IElectricityMeter electricityMeter,
                               IHomeBattery homeBattery, ICarCharger carCharger, NotificationService notificationService,
                               IWeatherProvider weatherProvider, HistoryService historyService)
    {
        Entities entities = new(ha);
        _electricityMeter = electricityMeter;
        _homeBattery = homeBattery;
        _carCharger = carCharger;
        _notificationService = notificationService;
        _weatherProvider = weatherProvider;

        WeatherForecast forecast = weatherProvider.GetWeatherAsync().GetAwaiter().GetResult();

        IReadOnlyList<HistoryEntry> history = historyService.GetEntityHistory(entities.Sensor.SolaxInverterPvPowerTotal.EntityId, DateTime.UtcNow.AddDays(-1)).GetAwaiter().GetResult();

        double totalWattSeconds = HistoryIntegrator.Integrate(history, DateTime.UtcNow.Date, DateTime.UtcNow);
        double totalkWh = (totalWattSeconds / 1000) / 3600;

        // We need to know how much power was used by the battery in total yesterday, minus whatever we might have used to charge the car.
        // The naive way is to just go through the battery history, but where the car was charging then insert a battery history record
        // for that time of 0W, and delete all other history records up until the charger current dropped back to 0, at which point insert
        // another battery record of 0W.


        // Initially run this very soon after start up
        scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () => await SetBatteryState(entities));

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () => await SetBatteryState(entities));

        // Car battery has changed current
        _carCharger.OnChargerCurrentChanged(async e =>
        {
            await SetBatteryState(entities);
        });

        // Home battery capacity has changed
        _homeBattery.OnBatteryChargePercentChanged(async _ =>
        {
            await SetBatteryState(entities);
        });

        // Listen for the import unit rate changing
        _electricityMeter.OnCurrentRatePerKwhChanged(async e =>
        {
            await SetBatteryState(entities);
        });
    }

    private async Task SetBatteryState(Entities entities)
    {

        WeatherForecast forecast = await _weatherProvider.GetWeatherAsync();

        // If the unit price is cheap and we have less than 50% in the battery, charge from the grid.
        double? currentUnitPriceRate = _electricityMeter.CurrentRatePerKwh;
        double? homeBatteryChargePct = _homeBattery.CurrentChargePercent;
        bool carIsCharging = _carCharger.ChargerCurrent > 1;
        bool electricityIsCheap = currentUnitPriceRate < 0.1;

        BatteryState currentHomeBatteryState = _homeBattery.GetHomeBatteryState();

        // Set the battery's max charging current, which is 50A minus whatever the car is drawing (gives us lots of headroom)
        double hypervoltCurrent = entities.Sensor.HypervoltChargerCurrent.State ?? 0;
        _homeBattery.SetMaxChargeCurrentHeadroom((int)hypervoltCurrent);

        BatteryState desiredHomeBatteryState = BatteryState.Unknown;

        if (carIsCharging && electricityIsCheap)
        {
            // Car is charging and the energy is cheap. Either charge the home battery, or don't use it.
            if (homeBatteryChargePct < 50)
            {
                // Start charging
                desiredHomeBatteryState = BatteryState.ForceCharging;
            }
            else if (currentHomeBatteryState == BatteryState.ForceCharging && homeBatteryChargePct < 60)
            {
                // If we're already charging then go up to 60%. Todo: work out the projected solar flux
                // to see how much lovely feree energy we can get from our friend the sun.
                desiredHomeBatteryState = BatteryState.ForceCharging;
            }
            else
            {
                // We've got enough energy in the battery, and since it's cheap we want to use the grid
                // for the car charging - save the battery for heating and general use when the rate
                // goes up later on in the day.
                desiredHomeBatteryState = BatteryState.Stopped;
            }
        }

        if (carIsCharging && !electricityIsCheap)
        {
            if (homeBatteryChargePct >= 20)
            {
                // Car is charging, and energy is expensive. Use the home battery if we can.
                // Save some for us to use though.
                desiredHomeBatteryState = BatteryState.NormalTOU;
            }
            else
            {
                // We've not got much in the home battery, so stop using it and save some.
                desiredHomeBatteryState = BatteryState.Stopped;
            }
        }

        if (!carIsCharging && electricityIsCheap)
        {
            // If we're generating no electricity, and it's nearly night, and we don't have enough juice to
            // get us through the night, then charge the battery.
            // Todo: We need to work out how much we're likely to need during the night. Look at how much
            // we used on previous days (minus car and battery charging) to project how much we'll need now.
            if (homeBatteryChargePct < 50)
            {
                DateTime nextDusk = DateTime.Parse(entities.Sun.Sun.Attributes?.NextDusk ?? "");
                DateTime nextDawn = DateTime.Parse(entities.Sun.Sun.Attributes?.NextDawn ?? "");
                if (DateTime.UtcNow.TimeOfDay > nextDusk.AddHours(-1).TimeOfDay
                 || DateTime.UtcNow.TimeOfDay < nextDawn.AddHours(1).TimeOfDay)
                {
                    desiredHomeBatteryState = BatteryState.ForceCharging;
                }
            }
        }

        if (!carIsCharging && !electricityIsCheap)
        {
            // Not charging the car, and the electricity is quite expensive, so set the battery to 
            // whatever the default schedule is (probably using it to power the house).
            desiredHomeBatteryState = BatteryState.NormalTOU;
        }

        if (desiredHomeBatteryState == BatteryState.Unknown)
        {
            desiredHomeBatteryState = BatteryState.NormalTOU;
        }

        // Charge the home battery too
        if (desiredHomeBatteryState != currentHomeBatteryState)
        {
            _homeBattery.SetHomeBatteryState(desiredHomeBatteryState);

            string title = "Home battery status changed";
            string message = $"Battery changed to {desiredHomeBatteryState}, unit price £{currentUnitPriceRate}, Hypervolt current {hypervoltCurrent}A, home battery {homeBatteryChargePct}%";
            _notificationService.SendPersistentNotification(title, message);
        }

    }
}