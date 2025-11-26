using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Weather;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
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
    private readonly ILogger<HomeBatteryManager> _logger;

    public HomeBatteryManager(IScheduler scheduler, IElectricityMeter electricityMeter,
                               IHomeBattery homeBattery, ICarCharger carCharger, NotificationService notificationService,
                               IWeatherProvider weatherProvider, ISolarPanels solarPanels,
                               ILogger<HomeBatteryManager> logger)
    {
        _electricityMeter = electricityMeter;
        _homeBattery = homeBattery;
        _carCharger = carCharger;
        _notificationService = notificationService;
        _weatherProvider = weatherProvider;
        _solarPanels = solarPanels;
        _logger = logger;

        // Initially run this very soon after start up
        scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () => await SetBatteryState());

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () => await SetBatteryState());

        // Car battery has changed current
        _carCharger.OnChargerCurrentChanged(async _ =>
        {
            await SetBatteryState();
        });

        // Home battery capacity has changed
        _homeBattery.OnBatteryChargePercentChanged(async _ =>
        {
            await SetBatteryState();
        });

        // The battery use mode has changed, possibly by the battery's own management logic,
        // or entering TOU mode according to a schedule.
        _homeBattery.OnBatteryUseModeChanged(async () =>
        {
            await SetBatteryState();
        });

        // Listen for the import unit rate changing
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ =>
        {
            await SetBatteryState();
        });
    }

    private readonly SemaphoreSlim _setBatteryStateSemaphore = new(1, 1);

    private double? _previousUnitPriceRate = null;
    private double? _previousHomeBatteryChargePct = null;
    private bool _previousIsCarCharging = false;
    private bool _previousIsElectricityCheap = false;
    private bool _previousIsInDischargeTimeRange = false;
    private BatteryState _previousBatteryState = BatteryState.Unknown;

    private async Task SetBatteryState()
    {
        try
        {
            await _setBatteryStateSemaphore.WaitAsync();
            TimeOnly dischargeAfter = new(21, 00);
            TimeOnly dischargeUntil = new(23, 00);

            double? currentUnitPriceRate = _electricityMeter.CurrentRatePerKwh;
            double? homeBatteryChargePct = _homeBattery.CurrentChargePercent;
            bool isCarCharging = _carCharger.ChargerCurrent > 1;
            bool isElectricityCheap = currentUnitPriceRate < 0.1;
            bool isInDischargeRange = DateTime.Now.TimeOfDay >= dischargeAfter.ToTimeSpan() && DateTime.Now.TimeOfDay <= dischargeUntil.ToTimeSpan();
            BatteryState currentHomeBatteryState = _homeBattery.GetHomeBatteryState();

            if (currentUnitPriceRate == _previousUnitPriceRate
                && homeBatteryChargePct == _previousHomeBatteryChargePct
                && isCarCharging == _previousIsCarCharging
                && isElectricityCheap == _previousIsElectricityCheap
                && currentHomeBatteryState == _previousBatteryState
                && isInDischargeRange == _previousIsInDischargeTimeRange)
            {
                // Nothing we care about has changed, so no need to do anything.
                return;
            }

            // Set the battery's max charging current, which is 50A minus whatever the car is drawing (gives us lots of headroom)
            double hypervoltCurrent = _carCharger.ChargerCurrent ?? 0;
            _homeBattery.SetMaxChargeCurrentHeadroom((int)hypervoltCurrent);

            BatteryPredictionResult batteryPrediction = await GetPredictedBatteryScores();

            const int startChargingIfMaxUnderPercent = 85;
            const int keepChargingIfMaxUnderPercent = 99;

            const int startChargingIfMinUnderPercent = 20;
            const int keepChargingIfMinUnderPercent = 25;

            const int stopDischargeIfUnderPercent = 25;

            const int batteryConsideredFullIfGtEqToPercent = 99;

            bool isBatteryProjectionInRangeThatNeedsCharging = batteryPrediction.MaximumChargePct < startChargingIfMaxUnderPercent || batteryPrediction.MinimumChargePct < startChargingIfMinUnderPercent
                    || (currentHomeBatteryState == BatteryState.ForceCharging && (batteryPrediction.MaximumChargePct < keepChargingIfMaxUnderPercent || batteryPrediction.MinimumChargePct < keepChargingIfMinUnderPercent));
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
            else if (!isElectricityCheap && isCarCharging && batteryPrediction.MinimumChargePct > 20)
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
            else if (isInDischargeRange && homeBatteryChargePct > stopDischargeIfUnderPercent)
            {
                // Let's discharge whatever is in the battery
                desiredHomeBatteryState = BatteryState.ForceDischarging;
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

                _logger.LogInformation(
                    "Battery state changed:\n" +
                    " * Home battery on {HomeBatteryChargePct}% (was {PreviousHomeBatteryChargePct}%)\n" +
                    " * Predicted 24 hour range from {MinimumProjectedPercent}% at {MinimumPctDate} to {MaximumProjectedPercent}% at {MaximumPctDate}\n" +
                    " * Predicted PV {EstimatedPV}kWh, predicted usage {EstimatedUsage}kWh\n" +
                    " * Battery state changed from {CurrentHomeBatteryState} to {DesiredHomeBatteryState}\n" +
                    " * Current unit price £{CurrentUnitPriceRate} (was £{PreviousUnitPriceRate})\n" +
                    " * Hypervolt current {HypervoltCurrent}A",
                    homeBatteryChargePct?.ToString("F0"),
                    _previousHomeBatteryChargePct?.ToString("F0"),
                    batteryPrediction.MinimumChargePct.ToString("F0"),
                    batteryPrediction.MinimumChargeDateTime.ToString("yyyy-MM-dd HH:mm"),
                    batteryPrediction.MaximumChargePct.ToString("F0"),
                    batteryPrediction.MaximumChargeDateTime.ToString("yyyy-MM-dd HH:mm"),
                    batteryPrediction.EstimatedSolarProductionkWh.ToString("F1"),
                    batteryPrediction.EstimatedUsagekWh.ToString("F1"),
                    currentHomeBatteryState,
                    desiredHomeBatteryState,
                    currentUnitPriceRate?.ToString("F3"),
                    _previousUnitPriceRate?.ToString("F3"),
                    hypervoltCurrent.ToString("F0")
                );
            }

            _previousHomeBatteryChargePct = homeBatteryChargePct;
            _previousIsCarCharging = isCarCharging;
            _previousIsElectricityCheap = isElectricityCheap;
            _previousUnitPriceRate = currentUnitPriceRate;
            _previousBatteryState = desiredHomeBatteryState;
            _previousIsInDischargeTimeRange = isInDischargeRange;
        }
        finally
        {
            _setBatteryStateSemaphore.Release();
        }
    }

    private IReadOnlyList<NumericHistoryEntry> _cachedSolarHistory = [];
    private IReadOnlyList<NumericHistoryEntry> _cachedTotalBatteryPowerChargeHistory = [];
    private IReadOnlyList<NumericHistoryEntry> _cachedCarChargerCurrentHistory = [];

    private class BatteryPredictionResult
    {
        public double MinimumChargekWh { get; set; }
        public double MaximumChargekWh { get; set; }
        public double MinimumChargePct { get; set; }
        public double MaximumChargePct { get; set; }
        public DateTime MinimumChargeDateTime { get; set; }
        public DateTime MaximumChargeDateTime { get; set; }
        public double Last24HoursSolarProductionkWh { get; set; }
        public double EstimatedSolarProductionkWh { get; set; }
        public double EstimatedSolarProductionPct { get; set; }
        public double Last24HoursUsagekWh { get; set; }
        public double EstimatedUsagekWh { get; set; }
        public double EstimatedUsagePct { get; set; }
    }

    private async Task<BatteryPredictionResult> GetPredictedBatteryScores()
    {
        const int daysPowerHistory = 3;

        BatteryPredictionResult result = new();

        DateTime now = DateTime.Now;
        DateTime solarHistoryStartDate = now.AddDays(-1);
        DateTime historyStartDate = now.Date.AddDays(-daysPowerHistory);
        DateTime historyEndDate = now;

        DateTime lastSolarHistoryDate = _cachedSolarHistory.LastOrDefault()?.LastChanged ?? solarHistoryStartDate;
        DateTime lastTotalBatteryPowerDate = _cachedTotalBatteryPowerChargeHistory.LastOrDefault()?.LastChanged ?? historyStartDate;
        DateTime lastCarChargerCurrentDate = _cachedCarChargerCurrentHistory.LastOrDefault()?.LastChanged ?? historyStartDate;

        // Get new data that we might now have available
        // Run all of the long running tasks in parallel.
        Task<WeatherForecast> getWeatherForecastTask = _weatherProvider.GetWeatherAsync();
        Task<IReadOnlyList<NumericHistoryEntry>> getSolarHistoryTask = _solarPanels.GetTotalSolarPanelPowerHistoryEntriesAsync(lastSolarHistoryDate, historyEndDate);

        // We need to know how much power was used by the battery in total yesterday, minus whatever we might have used to charge the car.
        // The naive way is to just go through the battery history, but where the car was charging then insert a battery history record
        // for that time of 0W, and delete all other history records up until the charger current dropped back to 0, at which point insert
        // another battery record of 0W.
        Task<IReadOnlyList<NumericHistoryEntry>> getTotalBatteryPowerChargeHistoryTask = _homeBattery.GetTotalBatteryPowerChargeHistoryEntriesAsync(lastTotalBatteryPowerDate, historyEndDate);

        // Get the car charger current history, sio we know which date ranges to exclude from the battery history.
        Task<IReadOnlyList<NumericHistoryEntry>> getCarChargerCurrentHistoryTask = _carCharger.GetChargerCurrentHistoryEntriesAsync(lastCarChargerCurrentDate, historyEndDate);

        // Get the home battery state history, so we can exclude times when it was charging or stopped.
        Task<IReadOnlyList<HistoryEntry<BatteryState>>> batteryStateHistoryTask = _homeBattery.GetBatteryStateHistoryEntriesAsync(lastTotalBatteryPowerDate, historyEndDate);

        await Task.WhenAll(getWeatherForecastTask, getSolarHistoryTask, getTotalBatteryPowerChargeHistoryTask, getCarChargerCurrentHistoryTask, batteryStateHistoryTask);

        WeatherForecast forecast = await getWeatherForecastTask;

        // Update the caches of the history data. We remove old stuff no longer needed, and add new stuff we don't already have.
        _cachedSolarHistory = [
            .. _cachedSolarHistory.Where(h => h.LastChanged > solarHistoryStartDate),
            .. (await getSolarHistoryTask).Where(h => h.LastChanged > lastSolarHistoryDate)
        ];

        _cachedTotalBatteryPowerChargeHistory = [
            .. _cachedTotalBatteryPowerChargeHistory.Where(h => h.LastChanged > historyStartDate),
            .. (await getTotalBatteryPowerChargeHistoryTask).Where(h => h.LastChanged > lastTotalBatteryPowerDate)
        ];

        _cachedCarChargerCurrentHistory = [
            .. _cachedCarChargerCurrentHistory.Where(h => h.LastChanged > historyStartDate),
            .. (await getCarChargerCurrentHistoryTask).Where(h => h.LastChanged > lastCarChargerCurrentDate)
        ];

        IReadOnlyList<HistoryEntry<BatteryState>> batteryStateHistory = await batteryStateHistoryTask;

        double totalWattSeconds = HistoryIntegrator.Integrate(_cachedSolarHistory, historyStartDate, historyEndDate);
        double totalkWh = (totalWattSeconds / 1000) / 3600;

        List<NumericHistoryEntry> nonCarChargingBatteryHistory = GetBatteryHistoryWithoutChargingAndCar(_cachedTotalBatteryPowerChargeHistory, _cachedCarChargerCurrentHistory, batteryStateHistory);

        double batteryCharge = (_homeBattery.CurrentChargePercent ?? 0) * _homeBattery.BatteryCapacitykWh / 100;
        Dictionary<DateTime, double> predictedBatteryChargeGraph = [];
        predictedBatteryChargeGraph[now] = batteryCharge;

        // Let's look at the weather for each of the next 24 hours, and see how much it'll compare to what we already know.
        for (int i = 0; i < 24; i++)
        {
            DateTime forecastTime = now.AddHours(i);
            DateTime historyTime = forecastTime.AddDays(-1);
            WeatherHour? weatherForecastEntry = forecast.Days.Where(f => f.DateLocal.ToDateTime(TimeOnly.MinValue) == forecastTime.Date)
                    .SelectMany(d => d.Hours?.Where(h => h.TimeLocal <= forecastTime && h.TimeLocal.AddHours(1) >= forecastTime) ?? [])
                    .FirstOrDefault();

            WeatherHour? weatherHistoryEntry = forecast.Days.Where(f => f.DateLocal.ToDateTime(TimeOnly.MinValue) == historyTime.Date)
                    .SelectMany(d => d.Hours?.Where(h => h.TimeLocal <= historyTime && h.TimeLocal.AddHours(1) >= historyTime) ?? [])
                    .FirstOrDefault();

            double pvForThisHourYesterday = HistoryIntegrator.Integrate(_cachedSolarHistory, historyTime, historyTime.AddHours(1)) / 3600000;
            double cloudYesterday = (weatherHistoryEntry?.CloudCover ?? 0) / 100d;
            double expectedCloudToday = (weatherForecastEntry?.CloudCover ?? 0) / 100d;
            double k = 1;
            double expectedPvForThisHourToday = pvForThisHourYesterday * Math.Pow(Math.E, -k * (expectedCloudToday - cloudYesterday));

            // Note: the figures come out as negative for discharge, but we care about amount discharged so flip it.
            IEnumerable<double> batteryDischargeHistoricValuesForThisHour = Enumerable.Range(0, daysPowerHistory).Select(i => HistoryIntegrator.Integrate(nonCarChargingBatteryHistory, historyTime.AddDays(-i), historyTime.AddDays(-i).AddHours(1)) / 3600000);
            double batteryDischargeAverageForThisHour = -batteryDischargeHistoricValuesForThisHour.Average();

            // The actual usage is what the battery discharged yesterday, plus whatever PV we had yesterday.
            double estimatedUsageHistoryForThisHour = batteryDischargeAverageForThisHour + pvForThisHourYesterday;

            batteryCharge += expectedPvForThisHourToday - estimatedUsageHistoryForThisHour;
            predictedBatteryChargeGraph[forecastTime.AddHours(1)] = batteryCharge;

            result.EstimatedSolarProductionkWh += expectedPvForThisHourToday;
            result.EstimatedUsagekWh += estimatedUsageHistoryForThisHour;
            result.Last24HoursSolarProductionkWh += pvForThisHourYesterday;
            result.Last24HoursUsagekWh += estimatedUsageHistoryForThisHour;
        }

        result.EstimatedSolarProductionPct = 100 * result.EstimatedSolarProductionkWh / _homeBattery.BatteryCapacitykWh;
        result.EstimatedUsagePct = 100 * result.EstimatedUsagekWh / _homeBattery.BatteryCapacitykWh;

        if (predictedBatteryChargeGraph.Count > 0)
        {
            KeyValuePair<DateTime, double> min = predictedBatteryChargeGraph.OrderBy(kv => kv.Value).First();
            result.MinimumChargekWh = min.Value;
            result.MinimumChargePct = 100 * min.Value / _homeBattery.BatteryCapacitykWh;
            result.MinimumChargeDateTime = min.Key;

            KeyValuePair<DateTime, double> max = predictedBatteryChargeGraph.OrderByDescending(kv => kv.Value).First();
            result.MaximumChargekWh = max.Value;
            result.MaximumChargePct = 100 * max.Value / _homeBattery.BatteryCapacitykWh;
            result.MaximumChargeDateTime = max.Key;
        }

        return result;
    }

    private static List<NumericHistoryEntry> GetBatteryHistoryWithoutChargingAndCar(
        IReadOnlyList<NumericHistoryEntry> batteryPowerHistory,
        IReadOnlyList<NumericHistoryEntry> carChargerHistory,
        IReadOnlyList<HistoryEntry<BatteryState>> batteryStateHistory)
    {
        // We only care about readings where it's negative, so the battery is discharging.
        List<NumericHistoryEntry> nonCarChargingBatteryHistory = [.. batteryPowerHistory.Select(h => new NumericHistoryEntry { LastChanged = h.LastChanged, State = Math.Min(h.State, 0) })];

        DateTime start = DateTime.MinValue, end = DateTime.MinValue;
        bool hasCurrent = false;
        for (int i = 0; i < carChargerHistory.Count; i++)
        {
            NumericHistoryEntry entry = carChargerHistory[i];
            double current = entry.State;
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

        // Remove where the battery was not in TOU mode.
        start = DateTime.MinValue;
        end = DateTime.MinValue;
        bool exclude = false;
        for (int i = 0; i < batteryStateHistory.Count; i++)
        {
            HistoryEntry<BatteryState> entry = batteryStateHistory[i];
            BatteryState state = entry.State;
            if (state != BatteryState.NormalTOU && !exclude)
            {
                exclude = true;
                start = entry.LastChanged;
            }
            else if (state == BatteryState.NormalTOU && exclude)
            {
                exclude = false;
                end = entry.LastChanged;
                nonCarChargingBatteryHistory = [
                    .. nonCarChargingBatteryHistory.Where(h => h.LastChanged < start || h.LastChanged > end),
                    new NumericHistoryEntry{ LastChanged = start, State = 0},
                    new NumericHistoryEntry{LastChanged = end, State = 0}
                ];
            }
        }

        nonCarChargingBatteryHistory = [.. nonCarChargingBatteryHistory.OrderBy(h => h.LastChanged)];
        return nonCarChargingBatteryHistory;
    }
}