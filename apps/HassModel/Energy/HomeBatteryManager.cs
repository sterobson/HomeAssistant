using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Weather;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ISolarPanels _solarPanels;

    public HomeBatteryManager(IScheduler scheduler, IElectricityMeter electricityMeter,
                               IHomeBattery homeBattery, ICarCharger carCharger, NotificationService notificationService,
                               IWeatherProvider weatherProvider, ISolarPanels solarPanels)
    {
        _electricityMeter = electricityMeter;
        _homeBattery = homeBattery;
        _carCharger = carCharger;
        _notificationService = notificationService;
        _weatherProvider = weatherProvider;
        _solarPanels = solarPanels;

        // Initially run this very soon after start up
        scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () => await SetBatteryState());

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () => await SetBatteryState());

        // Car battery has changed current
        _carCharger.OnChargerCurrentChanged(async e =>
        {
            await SetBatteryState();
        });

        // Home battery capacity has changed
        _homeBattery.OnBatteryChargePercentChanged(async _ =>
        {
            await SetBatteryState();
        });

        // Listen for the import unit rate changing
        _electricityMeter.OnCurrentRatePerKwhChanged(async e =>
        {
            await SetBatteryState();
        });
    }

    private async Task SetBatteryState()
    {
        // If the unit price is cheap and we have less than 50% in the battery, charge from the grid.
        double? currentUnitPriceRate = _electricityMeter.CurrentRatePerKwh;
        double? homeBatteryChargePct = _homeBattery.CurrentChargePercent;
        bool isCarCharging = _carCharger.ChargerCurrent > 1;
        bool isElectricityCheap = currentUnitPriceRate < 0.1;

        BatteryState currentHomeBatteryState = _homeBattery.GetHomeBatteryState();

        // Set the battery's max charging current, which is 50A minus whatever the car is drawing (gives us lots of headroom)
        double hypervoltCurrent = _carCharger.ChargerCurrent ?? 0;
        _homeBattery.SetMaxChargeCurrentHeadroom((int)hypervoltCurrent);

        (double minimumProjectedChargekWh, double maximumProjectedChargekWh) = (0, 0);
        if (isElectricityCheap)
        {
            (minimumProjectedChargekWh, maximumProjectedChargekWh) = await GetPredictedBatteryScores();
        }

        double minimumProjectedPercent = Math.Min(100 * minimumProjectedChargekWh / _homeBattery.BatteryCapacitykWh, homeBatteryChargePct ?? 0);
        double maximumProjectedPercent = Math.Max(100 * maximumProjectedChargekWh / _homeBattery.BatteryCapacitykWh, homeBatteryChargePct ?? 0);

        const int startChargingIfMaxUnderPercent = 85;
        const int keepChargingIfMaxUnderPercent = 90;

        const int startChargingIfMinUnderPercent = 20;
        const int keepChargingIfMinUnderPercent = 25;

        const int batteryConsideredFullIfGtEqToPercent = 99;

        bool isBatteryProjectionInRangeThatNeedsCharging = maximumProjectedPercent < startChargingIfMaxUnderPercent || minimumProjectedPercent < startChargingIfMinUnderPercent
                || (currentHomeBatteryState == BatteryState.ForceCharging && (maximumProjectedPercent < keepChargingIfMaxUnderPercent || minimumProjectedPercent < keepChargingIfMinUnderPercent));
        bool isBatteryFull = homeBatteryChargePct >= batteryConsideredFullIfGtEqToPercent;

        BatteryState desiredHomeBatteryState;
        if (isElectricityCheap && isBatteryProjectionInRangeThatNeedsCharging && !isBatteryFull)
        {
            // We need some more juice in the battery, and it's cheap to do.
            desiredHomeBatteryState = BatteryState.ForceCharging;
        }
        else if (isElectricityCheap && isCarCharging)
        {
            // We're charging the car and it's cheap energy, so don't use the battery.
            desiredHomeBatteryState = BatteryState.Stopped;
        }
        else if (!isElectricityCheap && isCarCharging && minimumProjectedPercent > 20)
        {
            // Car is charging, and energy is expensive. Use the home battery if we can.
            // Save some for us to use though.
            desiredHomeBatteryState = BatteryState.NormalTOU;
        }
        else if (isCarCharging)
        {
            // We're charging the car but it's some scenario where we don't want to use the battery, so pause it.
            desiredHomeBatteryState = BatteryState.Stopped;
        }
        else
        {
            // Not charging the car, nor any other special state, so set the battery to 
            // whatever the default schedule is (probably using it to power the house).
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

    private async Task<(double MinimumChargekWh, double MaximumChargekWh)> GetPredictedBatteryScores()
    {
        DateTime historyStartDate = DateTime.Today.AddDays(-1);
        DateTime historyEndDate = DateTime.Now;

        // Run all of the long running tasks in parallel.
        Task<WeatherForecast> getWeatherForecastTask = _weatherProvider.GetWeatherAsync();
        Task<IReadOnlyList<NumericHistoryEntry>> getSolarHistoryTask = _solarPanels.GetTotalSolarPanelPowerHistoryEntriesAsync(historyStartDate, historyEndDate);
        Task<IReadOnlyList<NumericHistoryEntry>> getTotalBatteryPowerChargeHistoryTask = _homeBattery.GetTotalBatteryPowerChargeHistoryEntriesAsync(historyStartDate, historyEndDate);
        Task<IReadOnlyList<HistoryEntry>> getCarChargerCurrentHistoryTask = _carCharger.GetChargerCurrentHistoryEntriesAsync(historyStartDate, historyEndDate);

        WeatherForecast forecast = await getWeatherForecastTask;
        IReadOnlyList<NumericHistoryEntry> solarHistory = await getSolarHistoryTask;

        double totalWattSeconds = HistoryIntegrator.Integrate(solarHistory, historyStartDate, historyEndDate);
        double totalkWh = (totalWattSeconds / 1000) / 3600;

        // We need to know how much power was used by the battery in total yesterday, minus whatever we might have used to charge the car.
        // The naive way is to just go through the battery history, but where the car was charging then insert a battery history record
        // for that time of 0W, and delete all other history records up until the charger current dropped back to 0, at which point insert
        // another battery record of 0W.
        IReadOnlyList<NumericHistoryEntry> batteryPowerHistory = await getTotalBatteryPowerChargeHistoryTask;

        // We only care about readings where it's negative, so the battery is discharging.
        List<NumericHistoryEntry> nonCarChargingBatteryHistory = [.. batteryPowerHistory.Select(h => new NumericHistoryEntry { LastChanged = h.LastChanged, State = Math.Min(h.State, 0) })];

        // Get the car charge history
        IReadOnlyList<HistoryEntry> carChargerHistory = await getCarChargerCurrentHistoryTask;

        DateTime start = DateTime.MinValue, end = DateTime.MinValue;
        bool hasCurrent = false;
        for (int i = 0; i < carChargerHistory.Count; i++)
        {
            HistoryEntry entry = carChargerHistory[i];
            double current = double.Parse(entry.State ?? "0");
            if (current > 1 && !hasCurrent)
            {
                hasCurrent = true;
                start = entry.LastChanged;
            }
            else if (current <= 1 && hasCurrent)
            {
                hasCurrent = false;
                end = entry.LastChanged;
                nonCarChargingBatteryHistory = [
                    .. nonCarChargingBatteryHistory.Where(h => h.LastChanged < start || h.LastChanged > end),
                    new NumericHistoryEntry{ LastChanged = start, State = 0},
                    new NumericHistoryEntry{LastChanged = end, State = 0}
                ];
            }
        }

        nonCarChargingBatteryHistory = [.. nonCarChargingBatteryHistory.OrderBy(h => h.LastChanged)];

        double batteryDischargeExcludingCarChargingkWh = HistoryIntegrator.Integrate(nonCarChargingBatteryHistory, historyStartDate, historyEndDate) / (1000 * 3600);

        double batteryCharge = (_homeBattery.CurrentChargePercent ?? 0) * _homeBattery.BatteryCapacitykWh / 100;
        Dictionary<DateTime, double> predictedBatteryChargeGraph = [];
        predictedBatteryChargeGraph[DateTime.Now] = batteryCharge;

        // Let's look at the weather for each of the next 24 hours, and see how much it'll compare to what we already know.
        for (int i = 0; i < 24; i++)
        {
            DateTime forecastTime = DateTime.Now.AddHours(i);
            DateTime historyTime = forecastTime.AddDays(-1);
            WeatherHour? forecastEntry = forecast.Days.Where(f => f.DateLocal.ToDateTime(TimeOnly.MinValue) == forecastTime.Date)
                    .SelectMany(d => d.Hours?.Where(h => h.TimeLocal <= forecastTime && h.TimeLocal.AddHours(1) >= forecastTime) ?? [])
                    .FirstOrDefault();

            WeatherHour? historyEntry = forecast.Days.Where(f => f.DateLocal.ToDateTime(TimeOnly.MinValue) == historyTime.Date)
                    .SelectMany(d => d.Hours?.Where(h => h.TimeLocal <= historyTime && h.TimeLocal.AddHours(1) >= historyTime) ?? [])
                    .FirstOrDefault();

            double pvYesterday = HistoryIntegrator.Integrate(solarHistory, historyTime, historyTime.AddHours(1)) / 3600000;
            double cloudYesterday = (historyEntry?.CloudCover ?? 0) / 100d;
            double expectedCloudToday = (forecastEntry?.CloudCover ?? 0) / 100d;
            double k = 1;
            double expectedPvToday = pvYesterday * Math.Pow(Math.E, -k * (expectedCloudToday - cloudYesterday));

            double batteryDischargeYesterday = HistoryIntegrator.Integrate(nonCarChargingBatteryHistory, historyTime, historyTime.AddHours(1)) / 3600000;
            batteryCharge += expectedPvToday + batteryDischargeYesterday;
            predictedBatteryChargeGraph[forecastTime.AddHours(1)] = batteryCharge;
        }

        if (predictedBatteryChargeGraph.Count > 0)
        {
            return (MinimumChargekWh: predictedBatteryChargeGraph.Values.Min(), MaximumChargekWh: predictedBatteryChargeGraph.Values.Max());
        }
        else
        {
            return (0, 0);
        }
    }
}