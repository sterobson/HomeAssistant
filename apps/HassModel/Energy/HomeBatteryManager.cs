using HomeAssistantGenerated;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Energy;

[NetDaemonApp]
internal class HomeBatteryManager
{
    private readonly IHaContext _ha;
    private readonly Services _services;

    public HomeBatteryManager(IHaContext ha, IScheduler scheduler)
    {
        Entities entities = new(ha);
        _services = new(ha);
        _ha = ha;

        double? maxDischargeCurrent = entities.Number.SolaxInverterBatteryDischargeMaxCurrent.State;
        double? chargePct = entities.Sensor.SolaxInverterBatteryCapacity.State;

        double? chargerCurrent = entities.Sensor.HypervoltChargerCurrent.State;

        // Initially run this very soon after start up
        scheduler.Schedule(TimeSpan.FromSeconds(new Random().Next(10, 60)), async () => await SetBatteryState(entities));

        // Run every 10 minutes, in case there's been a state change we somehow missed.
        scheduler.SchedulePeriodic(TimeSpan.FromMinutes(10), async () => await SetBatteryState(entities));

        // Car battery has changed current
        entities.Sensor.HypervoltChargerCurrent.StateChanges().SubscribeAsync(async e =>
        {
            await SetBatteryState(entities);
        });

        // Home battery capacity has changed
        entities.Sensor.SolaxInverterBatteryCapacity.StateChanges().SubscribeAsync(async e =>
        {
            await SetBatteryState(entities);
        });

        // Listen for the import unit rate changing
        entities.Sensor.OctopusEnergyElectricity24j04946911591015382045CurrentRate.StateChanges().SubscribeAsync(async e =>
        {
            await SetBatteryState(entities);
        });
    }

    private async Task SetBatteryState(Entities entities)
    {
        // If the unit price is cheap and we have less than 50% in the battery, charge from the grid.
        double? currentUnitPriceRate = entities.Sensor.OctopusEnergyElectricity24j04946911591015382045CurrentRate.State;
        double? homeBatteryChargePct = entities.Sensor.SolaxInverterBatteryCapacity.State;
        bool carIsCharging = entities.Sensor.HypervoltChargerCurrent.State > 1;

        BatteryState homeBatteryState = GetHomeBatteryState(entities);

        // Set the battery's max charging current, which is 50A minute whatever the car is drawing (gives us lots of headroom)
        double hypervoltCurrent = entities.Sensor.HypervoltChargerCurrent.State ?? 0;
        entities.Number.SolaxInverterBatteryChargeMaxCurrent.SetValue(((int)(50 - hypervoltCurrent)).ToString());

        BatteryState desiredHomeBatteryState = BatteryState.Unknown;

        if (carIsCharging && currentUnitPriceRate < 0.1)
        {
            // Car is charging and the energy is cheap. Charge the home battery, or don't use it.
            if (homeBatteryChargePct < 50)
            {
                desiredHomeBatteryState = BatteryState.ForceCharging;
            }
            else
            {
                desiredHomeBatteryState = BatteryState.Stopped;
            }
        }

        if (carIsCharging && currentUnitPriceRate >= 0.1)
        {
            if (homeBatteryChargePct >= 20)
            {
                // Car is charging, and energy is expensive. Use the home battery if we can.
                // Save some for us to use though.
                desiredHomeBatteryState = BatteryState.NormalTOU;
            }
            else
            {
                // We've not got much inthe home battery, so stop using it and save some.
                desiredHomeBatteryState = BatteryState.Stopped;
            }
        }

        if (!carIsCharging && currentUnitPriceRate < 0.1)
        {
            // If we're generating no electricity, and it's nearly night, and we don't have enough juice to
            // get us through the night, then charge the battery
            if (homeBatteryChargePct < 50)
            {
                DateTime nextDusk = DateTime.Parse(entities.Sun.Sun.Attributes?.NextDusk ?? "");
                if (DateTime.UtcNow.AddHours(1) > nextDusk)
                {
                    desiredHomeBatteryState = BatteryState.ForceCharging;
                }
            }
        }

        if (desiredHomeBatteryState == BatteryState.Unknown)
        {
            desiredHomeBatteryState = BatteryState.NormalTOU;
        }

        // Charge the home battery too
        if (desiredHomeBatteryState != homeBatteryState)
        {
            string title = "Home battery status changed";
            string message = $"Battery changed to {desiredHomeBatteryState}, unit price £{currentUnitPriceRate}, Hypervolt current {hypervoltCurrent}A, home battery {homeBatteryChargePct}%";
            _ha.CallService("notify", "persistent_notification", data: new { message, title });

            switch (desiredHomeBatteryState)
            {
                case BatteryState.NormalTOU:
                case BatteryState.Unknown:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [entities.Select.SolaxInverterChargerUseMode.EntityId] },
                        option: "Smart Schedule"
                    );
                    break;

                case BatteryState.ForceCharging:
                case BatteryState.ForceDischarging:
                case BatteryState.Stopped:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [entities.Select.SolaxInverterChargerUseMode.EntityId] },
                        option: "Manual Mode"
                    );
                    break;
            }

            switch (desiredHomeBatteryState)
            {
                case BatteryState.ForceCharging:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [entities.Select.SolaxInverterManualModeSelect.EntityId] },
                        option: "Force Charge"
                    );
                    break;
                case BatteryState.ForceDischarging:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [entities.Select.SolaxInverterManualModeSelect.EntityId] },
                        option: "Force Discharge"
                    );
                    break;
                case BatteryState.Stopped:
                    _services.Select.SelectOption(
                        target: new() { EntityIds = [entities.Select.SolaxInverterManualModeSelect.EntityId] },
                        option: "Stop Charge and Discharge"
                    );
                    break;
            }
        }

    }

    private static BatteryState GetHomeBatteryState(Entities entities)
    {
        string? chargerUseMode = entities.Select.SolaxInverterChargerUseMode.State;
        string? chargerManualMode = entities.Select.SolaxInverterManualModeSelect.State;

        return chargerUseMode switch
        {
            "Smart Schedule" => BatteryState.NormalTOU,
            "Manual Mode" => chargerManualMode switch
            {
                "Stop Charge and Discharge" => BatteryState.Stopped,
                "Force Charge" => BatteryState.ForceCharging,
                "Force Discharge" => BatteryState.ForceDischarging,
                _ => BatteryState.Unknown,
            },
            _ => BatteryState.Unknown,
        };
    }

    private enum BatteryState
    {
        NormalTOU,
        ForceCharging,
        ForceDischarging,
        Stopped,
        Unknown
    }
}
