using HomeAssistant.apps.Energy;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
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
    private List<EnergyRate> _cachedImportRates = [];
    private List<EnergyRate> _cachedExportRates = [];
    private List<PricingSlot> _cachedPricingSlots = [];
    private DateTime _lastRatesRefresh = DateTime.MinValue;
    private const int _ratesRefreshIntervalMinutes = 30;
    internal const int HysteresisPercent = 2;

    private bool _initialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly SemaphoreSlim _setBatteryStateSemaphore = new(1, 1);

    private double? _previousUnitPriceRate = null;
    private double? _previousHomeBatteryChargePct = null;
    private bool _previousIsCarCharging = false;
    private string? _previousActiveZoneRuleId = null;
    private bool _previousIsBatteryAtTarget = false;
    private BatteryState _previousBatteryState = BatteryState.Unknown;
    private double? _graduatedInitialPercent = null;

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
            await EnsureInitializedAsync();
            await SetBatteryState("app startup");
        });

        // Listen for rule changes from the UI via SignalR
        _rulesPersistence.RulesUpdated += async () =>
        {
            try
            {
                _currentRules = await _rulesPersistence.GetRulesAsync();
                _logger.LogInformation("Battery zone rules updated via SignalR, now have {Count} rules", _currentRules.Rules.Count);
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
            await RefreshRatesIfStaleAsync();
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

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized)
                return;

            _logger.LogInformation("Battery control service initializing: loading rules and refreshing rates");
            await _deviceSettingsPersistence.StartAsync();
            await _rulesPersistence.StartAsync();
            _currentRules = await _rulesPersistence.GetRulesAsync();
            _logger.LogInformation("Loaded {Count} battery zone rules on startup", _currentRules.Rules.Count);
            await RefreshRatesAsync();
            _initialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    internal async Task RefreshRatesAsync()
    {
        try
        {
            DateTime now = _timeProvider.GetLocalNow().DateTime;
            DateTime dayStart = now.Date;
            DateTime dayEnd = dayStart.AddDays(1);

            _cachedImportRates = await _ratesReader.GetElectricityImportRatesAsync(dayStart.ToUniversalTime(), dayEnd.ToUniversalTime());
            _cachedExportRates = await _ratesReader.GetElectricityExportRatesAsync(dayStart.ToUniversalTime(), dayEnd.ToUniversalTime());
            _cachedPricingSlots = PricingSlot.FromEnergyRates(_cachedImportRates, _cachedExportRates, now);
            _lastRatesRefresh = DateTime.UtcNow;

            _logger.LogInformation("Refreshed rates: {ImportCount} import, {ExportCount} export rates, {SlotCount} pricing slots for {Date:yyyy-MM-dd}",
                _cachedImportRates.Count, _cachedExportRates.Count, _cachedPricingSlots.Count, dayStart);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh rate data");
        }
    }

    private async Task RefreshRatesIfStaleAsync()
    {
        if (_lastRatesRefresh.AddMinutes(_ratesRefreshIntervalMinutes) < DateTime.UtcNow)
        {
            await RefreshRatesAsync();
        }
    }

    internal ResolvedZone? GetActiveZone(int currentMinutes)
    {
        List<ResolvedZone> zones = BatteryZoneResolver.ResolveAllZones(_currentRules, _cachedPricingSlots);
        return FindBestZone(zones, currentMinutes);
    }

    internal static ResolvedZone? FindBestZone(List<ResolvedZone> zones, int currentMinutes)
    {
        ResolvedZone? best = null;
        foreach (ResolvedZone zone in zones)
        {
            if (!BatteryZoneResolver.IsMinuteInZone(currentMinutes, zone.StartMinutes, zone.EndMinutes))
                continue;

            if (best == null)
            {
                best = zone;
                continue;
            }

            // Priority 1: Smart zones override fixed zones
            if (zone.IsSmart != best.IsSmart)
            {
                if (zone.IsSmart) best = zone;
                continue;
            }

            // Priority 2: Import overrides export (same smartness)
            if (zone.Action != best.Action)
            {
                if (zone.Action == BatteryZoneAction.Import) best = zone;
                continue;
            }

            // Priority 3: Higher target percent wins (same smartness + action)
            if (zone.TargetPercent > best.TargetPercent)
            {
                best = zone;
            }
        }
        return best;
    }

    public async Task SetBatteryState(string trigger)
    {
        await EnsureInitializedAsync();

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

            // Resolve zones from current rules + pricing slots and find the active zone
            List<ResolvedZone> resolvedZones = BatteryZoneResolver.ResolveAllZones(_currentRules, _cachedPricingSlots);
            ResolvedZone? activeZone = FindBestZone(resolvedZones, currentMinutes);
            string? activeZoneRuleId = activeZone?.RuleId;

            // Graduated target: interpolate from initial battery % toward target over zone duration
            double effectiveTargetPercent = activeZone?.TargetPercent ?? 0;
            if (activeZone != null && activeZone.GraduatedTarget && homeBatteryChargePct.HasValue)
            {
                // Only capture the initial percent when first entering this zone
                if (activeZoneRuleId != _previousActiveZoneRuleId || !_graduatedInitialPercent.HasValue)
                {
                    ResolvedZone? precedingZone = resolvedZones.Find(z => z != activeZone && z.EndMinutes == activeZone.StartMinutes);
                    _graduatedInitialPercent = precedingZone != null
                        ? precedingZone.TargetPercent
                        : activeZone.Action == BatteryZoneAction.Export ? 100 : 0;
                }

                int elapsed = BatteryZoneResolver.GetElapsedMinutes(currentMinutes, activeZone.StartMinutes, activeZone.EndMinutes);
                int totalDuration = activeZone.EndMinutes - activeZone.StartMinutes;
                double progress = Math.Clamp((double)elapsed / totalDuration, 0.0, 1.0);
                effectiveTargetPercent = _graduatedInitialPercent.Value + (activeZone.TargetPercent - _graduatedInitialPercent.Value) * progress;
            }

            bool isBatteryAtTarget = true;
            if (activeZone != null && homeBatteryChargePct.HasValue)
            {
                if (activeZone.Action == BatteryZoneAction.Import)
                {
                    isBatteryAtTarget = homeBatteryChargePct.Value >= effectiveTargetPercent;
                }
                else
                {
                    isBatteryAtTarget = homeBatteryChargePct.Value <= effectiveTargetPercent;
                }
            }

            //if (currentUnitPriceRate == _previousUnitPriceRate
            //    && homeBatteryChargePct == _previousHomeBatteryChargePct
            //    && isCarCharging == _previousIsCarCharging
            //    && currentHomeBatteryState == _previousBatteryState
            //    && activeZoneRuleId == _previousActiveZoneRuleId
            //    && isBatteryAtTarget)
            //{
            //    return;
            //}

            BatteryState desiredHomeBatteryState;

            // We're inside an active zone - use zone-driven logic with hysteresis
            if (activeZone?.Action == BatteryZoneAction.Import)
            {
                // Zone says charge (import)
                if (homeBatteryChargePct >= effectiveTargetPercent)
                {
                    // At or above target - stop charging
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
                else if (isBatteryCharging)
                {
                    // Already charging and below target - keep charging
                    desiredHomeBatteryState = BatteryState.ForceCharging;
                }
                else if (homeBatteryChargePct <= effectiveTargetPercent - HysteresisPercent)
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
                if (homeBatteryChargePct <= effectiveTargetPercent)
                {
                    // At or below target - stop discharging
                    desiredHomeBatteryState = isCarCharging ? BatteryState.Stopped : BatteryState.NormalTOU;
                }
                else if (isBatterySelling)
                {
                    // Already discharging and above target - keep discharging
                    desiredHomeBatteryState = BatteryState.ForceDischarging;
                }
                else if (homeBatteryChargePct >= effectiveTargetPercent + HysteresisPercent)
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
                        ? $"{activeZone.Action} to {effectiveTargetPercent:F0}% ({activeZone.StartMinutes / 60:00}:{activeZone.StartMinutes % 60:00}-{activeZone.EndMinutes % 1440 / 60:00}:{activeZone.EndMinutes % 1440 % 60:00})"
                        : "none",
                    currentHomeBatteryState,
                    desiredHomeBatteryState,
                    currentUnitPriceRate?.ToString("F3"),
                    _previousUnitPriceRate?.ToString("F3"),
                    hypervoltCurrent.ToString("F0"),
                    _currentRules.Rules.Count,
                    resolvedZones.Count,
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
            await RefreshRatesAsync();
            return;
        }

        try
        {
            // Sensor reports in £/kWh, pricing slots use p/kWh — convert to pence
            double sensorPencePerKwh = newImportRate.Value * 100;

            DateTime now = _timeProvider.GetLocalNow().DateTime;
            int currentMinutes = now.Hour * 60 + now.Minute;

            List<PricingSlot> slots = PricingSlot.FromEnergyRatesExact(_cachedImportRates, _cachedExportRates, now);

            int overrideEndMinutes = PriceAnalysis.DetermineOverrideEndMinutes(
                _cachedImportRates, now, sensorPencePerKwh);

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
                existingAtStart.ImportPrice = sensorPencePerKwh;
            }
            else
            {
                slots.Add(new PricingSlot
                {
                    TimeMinutes = currentMinutes,
                    ImportPrice = sensorPencePerKwh,
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

            _cachedPricingSlots = slots.OrderBy(s => s.TimeMinutes).ToList();

            List<ResolvedZone> resolvedZones = BatteryZoneResolver.ResolveAllZones(_currentRules, _cachedPricingSlots);
            _logger.LogInformation(
                "Applied rate override {NewRate}p/kWh: override minutes {Start}-{End}, {ZoneCount} zones resolved",
                sensorPencePerKwh, currentMinutes, overrideEndMinutes, resolvedZones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply rate override, falling back to full rate refresh");
            await RefreshRatesAsync();
        }
    }

    /// <summary>
    /// Mark the service as already initialized, skipping the startup sequence.
    /// Used by tests that pre-configure rules and rates directly.
    /// </summary>
    internal void MarkAsInitialized() => _initialized = true;

    /// <summary>
    /// Inject rules for testing, bypassing the persistence service.
    /// </summary>
    internal void SetCurrentRules(BatteryZoneRules rules)
    {
        _currentRules = rules;
    }

    /// <summary>
    /// Inject cached rates and rebuild pricing slots for testing.
    /// </summary>
    internal void SetCachedRates(List<EnergyRate> importRates, List<EnergyRate> exportRates)
    {
        _cachedImportRates = importRates;
        _cachedExportRates = exportRates;
    }

    /// <summary>
    /// Inject cached pricing slots directly for testing.
    /// </summary>
    internal void SetCachedPricingSlots(List<PricingSlot> slots)
    {
        _cachedPricingSlots = slots;
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
        _graduatedInitialPercent = null;
    }

    /// <summary>
    /// Pre-set the graduated zone state for testing, so the service treats the zone as already entered
    /// with the given initial battery percent, bypassing the automatic capture on first evaluation.
    /// </summary>
    internal void SetGraduatedZoneState(string zoneRuleId, double initialPercent)
    {
        _previousActiveZoneRuleId = zoneRuleId;
        _graduatedInitialPercent = initialPercent;
    }
}
