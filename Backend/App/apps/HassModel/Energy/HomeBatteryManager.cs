using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
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
    private readonly ILogger<HomeBatteryManager> _logger;
    private readonly TimeProvider _timeProvider;

    public HomeBatteryManager(IScheduler scheduler, IElectricityMeter electricityMeter,
                               IHomeBattery homeBattery, ICarCharger carCharger,
                               ILogger<HomeBatteryManager> logger, TimeProvider timeProvider)
    {
        _electricityMeter = electricityMeter;
        _homeBattery = homeBattery;
        _carCharger = carCharger;
        _logger = logger;
        _timeProvider = timeProvider;

        // Initially run this very soon after start up
        scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () => await SetBatteryState("app startup"));

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () => await SetBatteryState("periodic check"));

        // Car battery has changed current
        _carCharger.OnChargerCurrentChanged(async _ =>
        {
            await SetBatteryState("car charger current changed");
        });

        // Home battery capacity has changed
        _homeBattery.OnBatteryChargePercentChanged(async _ =>
        {
            await SetBatteryState("battery percent changed");
        });

        // The battery use mode has changed, possibly by the battery's own management logic,
        // or entering TOU mode according to a schedule.
        _homeBattery.OnBatteryUseModeChanged(async () =>
        {
            await SetBatteryState("battery use mode changed");
        });

        // Listen for the import unit rate changing
        _electricityMeter.OnCurrentRatePerKwhChanged(async _ =>
        {
            await SetBatteryState("import rate changed");
        });
    }

    private readonly SemaphoreSlim _setBatteryStateSemaphore = new(1, 1);

    private double? _previousUnitPriceRate = null;
    private double? _previousHomeBatteryChargePct = null;
    private bool _previousIsCarCharging = false;
    private bool _previousIsElectricityCheap = false;
    private bool _previousIsBatteryChargeAboveTarget = false;
    private BatteryState _previousBatteryState = BatteryState.Unknown;

    private async Task SetBatteryState(string trigger)
    {
        try
        {
            await _setBatteryStateSemaphore.WaitAsync();

            TimeOnly dischargeUntil = new(23, 30);
            const int stopDischargeIfUnderPercent = 20;
            const int batteryConsideredFullIfGtEqToPercent = 99;
            const int onlyStartChargingBatteryIfBelow = batteryConsideredFullIfGtEqToPercent - 4; // Stop the flapping when charging and battery is nearly full

            double? currentUnitPriceRate = _electricityMeter.CurrentRatePerKwh;
            double? homeBatteryChargePct = _homeBattery.CurrentChargePercent;
            bool isCarCharging = _carCharger.ChargerCurrent > 1;

            // Todo: electricity is only cheap if the unit rate is in the bottom 25% of prices of the last 24 hours, or
            // if the car charger is being controlled by Octopus and we're within one of its charging schedules.
            bool isElectricityCheap = currentUnitPriceRate < 0.1;

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            BatteryState currentHomeBatteryState = _homeBattery.GetHomeBatteryState();

            bool isBatteryCharging = currentHomeBatteryState == BatteryState.ForceCharging;
            bool isBatterySelling = currentHomeBatteryState == BatteryState.ForceDischarging;

            // Set the battery's max charging current, which is 50A minus whatever the car is drawing (gives us lots of headroom)
            double hypervoltCurrent = _carCharger.ChargerCurrent ?? 0;
            _homeBattery.SetMaxChargeCurrentHeadroom((int)hypervoltCurrent);

            // Target levels: 100% at 8am, 80% at midday, 20% at 11pm
            Dictionary<TimeOnly, double> targetBatteryLevels = new()
            {
                // Expect to be at 100% at midnight - won't happen, but this stops us using the battery at other times.
                { new TimeOnly(0, 0), 100 },
                // After breakfast, hopefully a decent amount left.
                { new TimeOnly(8, 0), 95 },
                // Midday - target is 90%, at which point we sell back to the grid until down to 80%
                { new TimeOnly(12, 0), 80 + (!isBatterySelling ? 10 : 0) },
                // Aim to have drained the battery by 23:30. If got more than 2% above target left, reduce the target by 2% to avoid flapping.
                { dischargeUntil, stopDischargeIfUnderPercent - ((isBatterySelling && (_homeBattery.CurrentChargePercent > stopDischargeIfUnderPercent + 2)) ? 2 : 0) }
            };

            double homeBatteryTargetChargePctRightNow = GetTargetBatteryLevel(TimeOnly.FromDateTime(now), targetBatteryLevels);
            bool isBatteryChargeAboveTarget = _homeBattery.CurrentChargePercent > homeBatteryTargetChargePctRightNow;

            if (currentUnitPriceRate == _previousUnitPriceRate
                && homeBatteryChargePct == _previousHomeBatteryChargePct
                && isCarCharging == _previousIsCarCharging
                && isElectricityCheap == _previousIsElectricityCheap
                && currentHomeBatteryState == _previousBatteryState
                && isBatteryChargeAboveTarget == _previousIsBatteryChargeAboveTarget)
            {
                // Nothing we care about has changed, so no need to do anything.
                return;
            }

            BatteryState desiredHomeBatteryState;
            if (isElectricityCheap && homeBatteryChargePct < onlyStartChargingBatteryIfBelow && !isBatteryCharging)
            {
                // We're not currently charging, but the energy is cheap and the battery has < 95%, so start charging.
                desiredHomeBatteryState = BatteryState.ForceCharging;
            }
            else if (isElectricityCheap && homeBatteryChargePct < batteryConsideredFullIfGtEqToPercent && isBatteryCharging)
            {
                // We are already charging, energy is cheap, and the battery is not full yet, so keep topping up.
                // Even if the car is charging, it's more cost effective to charge both at once.
                desiredHomeBatteryState = BatteryState.ForceCharging;
            }
            else if (isElectricityCheap && isCarCharging)
            {
                // We're charging the car and it's cheap energy, so don't use the battery.
                desiredHomeBatteryState = BatteryState.Stopped;
            }
            else if (!isElectricityCheap && isCarCharging && isBatteryChargeAboveTarget)
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
            else if (isBatteryChargeAboveTarget)
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
                    " * Home battery target level right now {HomeBatteryTargetChargePct}%\n" +
                    " * Battery state changed from {CurrentHomeBatteryState} to {DesiredHomeBatteryState}\n" +
                    " * Current unit price £{CurrentUnitPriceRate} (was £{PreviousUnitPriceRate})\n" +
                    " * Hypervolt current {HypervoltCurrent}A\n" +
                    " * Triggered by {TriggeredBy}",
                    homeBatteryChargePct?.ToString("F0"),
                    _previousHomeBatteryChargePct?.ToString("F0"),
                    homeBatteryTargetChargePctRightNow.ToString("F0"),
                    currentHomeBatteryState,
                    desiredHomeBatteryState,
                    currentUnitPriceRate?.ToString("F3"),
                    _previousUnitPriceRate?.ToString("F3"),
                    hypervoltCurrent.ToString("F0"),
                    trigger
                );
            }

            _previousHomeBatteryChargePct = homeBatteryChargePct;
            _previousIsCarCharging = isCarCharging;
            _previousIsElectricityCheap = isElectricityCheap;
            _previousUnitPriceRate = currentUnitPriceRate;
            _previousBatteryState = desiredHomeBatteryState;
            _previousIsBatteryChargeAboveTarget = isBatteryChargeAboveTarget;
        }
        finally
        {
            _setBatteryStateSemaphore.Release();
        }
    }

    private static double GetTargetBatteryLevel(TimeOnly time, Dictionary<TimeOnly, double> targetBatteryLevels)
    {
        // Sort the dictionary by time
        List<KeyValuePair<TimeOnly, double>> sortedTargets = [.. targetBatteryLevels.OrderBy(kvp => kvp.Key)];

        // If the time is before or at the first entry, return the first value
        if (time <= sortedTargets[0].Key)
        {
            return sortedTargets[0].Value;
        }

        // If the time is after or at the last entry, return the last value
        if (time >= sortedTargets[^1].Key)
        {
            return sortedTargets[^1].Value;
        }

        // Find the two surrounding time points
        for (int i = 0; i < sortedTargets.Count - 1; i++)
        {
            TimeOnly t1 = sortedTargets[i].Key;
            TimeOnly t2 = sortedTargets[i + 1].Key;

            if (time >= t1 && time <= t2)
            {
                double v1 = sortedTargets[i].Value;
                double v2 = sortedTargets[i + 1].Value;

                // Calculate the interpolation factor
                double totalMinutes = (t2.ToTimeSpan() - t1.ToTimeSpan()).TotalMinutes;
                double elapsedMinutes = (time.ToTimeSpan() - t1.ToTimeSpan()).TotalMinutes;
                double factor = elapsedMinutes / totalMinutes;

                // Linear interpolation
                return v1 + (v2 - v1) * factor;
            }
        }

        // Should not reach here, but return a default value just in case
        return sortedTargets[^1].Value;
    }
}