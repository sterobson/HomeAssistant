using HomeAssistant.apps.Energy;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Energy;

internal class BatteryControlService
{
    private readonly IScheduler _scheduler;
    private readonly IElectricityMeter _electricityMeter;
    private readonly IHomeBattery _homeBattery;
    private readonly ICarCharger _carCharger;
    private readonly ILogger<BatteryControlService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IBatteryRulesPersistenceService _rulesPersistence;
    private readonly IElectricityRatesReader _ratesReader;
    private readonly IDeviceSettingsPersistenceService _deviceSettingsPersistence;

    private BatteryZoneRules _currentRules = new();
    private List<ResolvedZone> _resolvedZones = [];
    private List<EnergyRate> _cachedImportRates = [];
    private List<EnergyRate> _cachedExportRates = [];
    private DateTime _lastZoneResolution = DateTime.MinValue;
    private const int _zoneResolutionIntervalMinutes = 30;
    internal const int HysteresisPercent = 2;

    private readonly SemaphoreSlim _setBatteryStateSemaphore = new(1, 1);

    private double? _previousUnitPriceRate = null;
    private double? _previousHomeBatteryChargePct = null;
    private bool _previousIsCarCharging = false;
    private string? _previousActiveZoneRuleId = null;
    private bool _previousIsBatteryAtTarget = false;
    private BatteryState _previousBatteryState = BatteryState.Unknown;

    public BatteryControlService(IScheduler scheduler, IElectricityMeter electricityMeter,
                                 IHomeBattery homeBattery, ICarCharger carCharger,
                                 ILogger<BatteryControlService> logger, TimeProvider timeProvider,
                                 IBatteryRulesPersistenceService rulesPersistence,
                                 IElectricityRatesReader ratesReader,
                                 IDeviceSettingsPersistenceService deviceSettingsPersistence)
    {
        _scheduler = scheduler;
        _electricityMeter = electricityMeter;
        _homeBattery = homeBattery;
        _carCharger = carCharger;
        _logger = logger;
        _timeProvider = timeProvider;
        _rulesPersistence = rulesPersistence;
        _ratesReader = ratesReader;
        _deviceSettingsPersistence = deviceSettingsPersistence;
    }

    public void Start()
    {
        // Start the persistence service and load rules
        _scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () =>
        {
            await _deviceSettingsPersistence.StartAsync();
            await _rulesPersistence.StartAsync();
            _currentRules = await _rulesPersistence.GetRulesAsync();
            _logger.LogInformation("Loaded {Count} battery zone rules on startup", _currentRules.Rules.Count);
            await ResolveZonesAsync();
            await SetBatteryState("app startup");
        });

        // Listen for rule changes from the UI via SignalR
        _rulesPersistence.RulesUpdated += async () =>
        {
            try
            {
                _currentRules = await _rulesPersistence.GetRulesAsync();
                _logger.LogInformation("Battery zone rules updated via SignalR, now have {Count} rules", _currentRules.Rules.Count);
                await ResolveZonesAsync();
                await SetBatteryState("rules updated");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Ignoring RulesUpdated callback - app context has been disposed");
            }
        };

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        _scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () =>
        {
            await ResolveZonesIfStaleAsync();
            await SetBatteryState("periodic check");
        });

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
        _electricityMeter.OnCurrentRatePerKwhChanged(async change =>
        {
            await ReactToRateChangeAsync(change.New);
            await SetBatteryState("import rate changed");
        });
    }

    internal async Task ResolveZonesAsync()
    {
        try
        {
            DateTime now = _timeProvider.GetLocalNow().DateTime;
            DateTime dayStart = now.Date;
            DateTime dayEnd = dayStart.AddDays(1);

            List<EnergyRate> importRates = await _ratesReader.GetElectricityImportRatesAsync(dayStart.ToUniversalTime(), dayEnd.ToUniversalTime());
            List<EnergyRate> exportRates = await _ratesReader.GetElectricityExportRatesAsync(dayStart.ToUniversalTime(), dayEnd.ToUniversalTime());
            _cachedImportRates = importRates;
            _cachedExportRates = exportRates;

            List<PricingSlot> slots = PricingSlot.FromEnergyRates(importRates, exportRates, now);
            _resolvedZones = BatteryZoneResolver.ResolveAllZones(_currentRules, slots);
            _lastZoneResolution = DateTime.UtcNow;

            _logger.LogInformation("Resolved {ZoneCount} zones from {RuleCount} rules for {Date:yyyy-MM-dd}",
                _resolvedZones.Count, _currentRules.Rules.Count, dayStart);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve battery zones from pricing data");
        }
    }

    private async Task ResolveZonesIfStaleAsync()
    {
        if (_lastZoneResolution.AddMinutes(_zoneResolutionIntervalMinutes) < DateTime.UtcNow)
        {
            await ResolveZonesAsync();
        }
    }

    internal ResolvedZone? GetActiveZone(int currentMinutes)
    {
        return _resolvedZones.FirstOrDefault(z =>
            currentMinutes >= z.StartMinutes && currentMinutes < z.EndMinutes);
    }

    public async Task SetBatteryState(string trigger)
    {
        try
        {
            await _setBatteryStateSemaphore.WaitAsync();

            double? currentUnitPriceRate = _electricityMeter.CurrentRatePerKwh;
            double? homeBatteryChargePct = _homeBattery.CurrentChargePercent;
            bool isCarCharging = _carCharger.ChargerCurrent > 1;

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            int currentMinutes = now.Hour * 60 + now.Minute;
            BatteryState currentHomeBatteryState = _homeBattery.GetHomeBatteryState();

            bool isBatteryCharging = currentHomeBatteryState == BatteryState.ForceCharging;
            bool isBatterySelling = currentHomeBatteryState == BatteryState.ForceDischarging;

            // Set the battery's max charging current, which is 50A minus whatever the car is drawing
            double hypervoltCurrent = _carCharger.ChargerCurrent ?? 0;
            _homeBattery.SetMaxChargeCurrentHeadroom((int)hypervoltCurrent);

            // Find the active zone for the current time
            ResolvedZone? activeZone = GetActiveZone(currentMinutes);
            string? activeZoneRuleId = activeZone?.RuleId;

            bool isBatteryAtTarget = false;
            if (activeZone != null && homeBatteryChargePct.HasValue)
            {
                if (activeZone.Action == BatteryZoneAction.Import)
                {
                    isBatteryAtTarget = homeBatteryChargePct.Value >= activeZone.TargetPercent;
                }
                else
                {
                    isBatteryAtTarget = homeBatteryChargePct.Value <= activeZone.TargetPercent;
                }
            }

            if (currentUnitPriceRate == _previousUnitPriceRate
                && homeBatteryChargePct == _previousHomeBatteryChargePct
                && isCarCharging == _previousIsCarCharging
                && currentHomeBatteryState == _previousBatteryState
                && activeZoneRuleId == _previousActiveZoneRuleId
                && isBatteryAtTarget == _previousIsBatteryAtTarget)
            {
                return;
            }

            BatteryState desiredHomeBatteryState;

            // We're inside an active zone - use zone-driven logic with hysteresis
            if (activeZone?.Action == BatteryZoneAction.Import)
            {
                // Zone says charge (import)
                if (homeBatteryChargePct >= activeZone.TargetPercent)
                {
                    // At or above target - stop charging
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
                else if (isBatteryCharging)
                {
                    // Already charging and below target - keep charging
                    desiredHomeBatteryState = BatteryState.ForceCharging;
                }
                else if (homeBatteryChargePct <= activeZone.TargetPercent - HysteresisPercent)
                {
                    // 2%+ below target and not charging - start charging
                    desiredHomeBatteryState = BatteryState.ForceCharging;
                }
                else
                {
                    // Within hysteresis band and not charging - don't start
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
            }
            else if (activeZone?.Action == BatteryZoneAction.Export)
            {
                // Zone says discharge (export)
                if (homeBatteryChargePct <= activeZone.TargetPercent)
                {
                    // At or below target - stop discharging
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
                else if (isBatterySelling)
                {
                    // Already discharging and above target - keep discharging
                    desiredHomeBatteryState = BatteryState.ForceDischarging;
                }
                else if (homeBatteryChargePct >= activeZone.TargetPercent + HysteresisPercent)
                {
                    // 2%+ above target and not discharging - start discharging
                    desiredHomeBatteryState = BatteryState.ForceDischarging;
                }
                else
                {
                    // Within hysteresis band and not discharging - don't start
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
            }
            else if (isCarCharging)
            {
                // No active zone, car is charging - pause the battery
                desiredHomeBatteryState = BatteryState.Stopped;
            }
            else
            {
                // No active zone, no car charging - normal TOU
                desiredHomeBatteryState = BatteryState.NormalTOU;
            }

            if (desiredHomeBatteryState != currentHomeBatteryState)
            {
                _homeBattery.SetHomeBatteryState(desiredHomeBatteryState);

                _logger.LogInformation(
                    "Battery state changed:\n" +
                    " * Home battery on {HomeBatteryChargePct}% (was {PreviousHomeBatteryChargePct}%)\n" +
                    " * Active zone: {ActiveZone}\n" +
                    " * Battery state changed from {CurrentHomeBatteryState} to {DesiredHomeBatteryState}\n" +
                    " * Current unit price £{CurrentUnitPriceRate} (was £{PreviousUnitPriceRate})\n" +
                    " * Hypervolt current {HypervoltCurrent}A\n" +
                    " * Zone rules: {ZoneRuleCount}, resolved zones: {ResolvedZoneCount}\n" +
                    " * Triggered by {TriggeredBy}",
                    homeBatteryChargePct?.ToString("F0"),
                    _previousHomeBatteryChargePct?.ToString("F0"),
                    activeZone != null
                        ? $"{activeZone.Action} to {activeZone.TargetPercent}% ({activeZone.StartMinutes / 60:00}:{activeZone.StartMinutes % 60:00}-{activeZone.EndMinutes / 60:00}:{activeZone.EndMinutes % 60:00})"
                        : "none",
                    currentHomeBatteryState,
                    desiredHomeBatteryState,
                    currentUnitPriceRate?.ToString("F3"),
                    _previousUnitPriceRate?.ToString("F3"),
                    hypervoltCurrent.ToString("F0"),
                    _currentRules.Rules.Count,
                    _resolvedZones.Count,
                    trigger
                );
            }

            _previousHomeBatteryChargePct = homeBatteryChargePct;
            _previousIsCarCharging = isCarCharging;
            _previousUnitPriceRate = currentUnitPriceRate;
            _previousBatteryState = desiredHomeBatteryState;
            _previousActiveZoneRuleId = activeZoneRuleId;
            _previousIsBatteryAtTarget = isBatteryAtTarget;
        }
        finally
        {
            _setBatteryStateSemaphore.Release();
        }
    }

    internal async Task ReactToRateChangeAsync(double? newImportRate)
    {
        if (newImportRate == null || _cachedImportRates.Count == 0)
        {
            await ResolveZonesAsync();
            return;
        }

        try
        {
            DateTime now = _timeProvider.GetLocalNow().DateTime;
            int currentMinutes = now.Hour * 60 + now.Minute;

            List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(_cachedImportRates, _cachedExportRates, now);

            int overrideEndMinutes = PriceAnalysis.DetermineOverrideEndMinutes(
                _cachedImportRates, now, newImportRate.Value);

            // Look up published rates at key times before modifying the list
            double exportAtStart = slots
                .Where(s => s.TimeMinutes <= currentMinutes)
                .LastOrDefault()?.ExportPrice ?? 0;

            PricingSlot? publishedAtEnd = slots
                .Where(s => s.TimeMinutes <= overrideEndMinutes)
                .LastOrDefault();

            // Remove any published boundaries inside the override window
            slots.RemoveAll(s => s.TimeMinutes > currentMinutes && s.TimeMinutes < overrideEndMinutes);

            // Insert override start data point
            PricingSlot? existingAtStart = slots.FirstOrDefault(s => s.TimeMinutes == currentMinutes);
            if (existingAtStart != null)
            {
                existingAtStart.ImportPrice = newImportRate.Value;
            }
            else
            {
                slots.Add(new PricingSlot
                {
                    TimeMinutes = currentMinutes,
                    ImportPrice = newImportRate.Value,
                    ExportPrice = exportAtStart
                });
            }

            // Insert revert data point (back to published rate)
            if (overrideEndMinutes < 1440)
            {
                PricingSlot? existingAtEnd = slots.FirstOrDefault(s => s.TimeMinutes == overrideEndMinutes);
                if (existingAtEnd == null)
                {
                    slots.Add(new PricingSlot
                    {
                        TimeMinutes = overrideEndMinutes,
                        ImportPrice = publishedAtEnd?.ImportPrice ?? 0,
                        ExportPrice = publishedAtEnd?.ExportPrice ?? 0
                    });
                }
            }

            slots = slots.OrderBy(s => s.TimeMinutes).ToList();
            _resolvedZones = BatteryZoneResolver.ResolveAllZones(_currentRules, slots);

            _logger.LogInformation(
                "Reactively re-resolved zones for rate change to {NewRate}: override minutes {Start}-{End}, {ZoneCount} zones",
                newImportRate.Value, currentMinutes, overrideEndMinutes, _resolvedZones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactively re-resolve zones, falling back to full resolution");
            await ResolveZonesAsync();
        }
    }

    /// <summary>
    /// Inject pre-resolved zones for testing, bypassing the async zone resolution.
    /// </summary>
    internal void SetResolvedZones(List<ResolvedZone> zones)
    {
        _resolvedZones = zones;
    }

    /// <summary>
    /// Inject rules for testing, bypassing the persistence service.
    /// </summary>
    internal void SetCurrentRules(BatteryZoneRules rules)
    {
        _currentRules = rules;
    }

    /// <summary>
    /// Inject cached rates for testing, so ReactToRateChangeAsync can work without API calls.
    /// </summary>
    internal void SetCachedRates(List<EnergyRate> importRates, List<EnergyRate> exportRates)
    {
        _cachedImportRates = importRates;
        _cachedExportRates = exportRates;
    }

    /// <summary>
    /// Clear previous state tracking so the next SetBatteryState call is guaranteed to evaluate.
    /// </summary>
    internal void ResetChangeDetection()
    {
        _previousUnitPriceRate = null;
        _previousHomeBatteryChargePct = null;
        _previousIsCarCharging = false;
        _previousActiveZoneRuleId = null;
        _previousIsBatteryAtTarget = false;
        _previousBatteryState = BatteryState.Unknown;
    }
}
