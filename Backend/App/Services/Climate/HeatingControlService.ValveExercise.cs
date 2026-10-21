using HomeAssistant.apps;
using HomeAssistant.Shared;
using NetDaemon.Extensions.Scheduler;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// The monthly boiler and radiator valve exercise (owner, 2026-10-19, after finding a TRV seized from months of not
/// moving). At 08:00 on the 1st every radiator valve is switched to the opposite of what it is (open to shut, shut
/// to open) and the boiler run, then 2½ minutes later each valve is put back to its exact setpoint, and at 5
/// minutes the boiler goes back to what the rooms want. If the boiler is already on, the heating is evidently
/// working and that month's exercise is skipped. Normal evaluations wait while it runs, so they cannot undo it.
/// In boiler-thermostat mode the app cannot drive the boiler, so only the valves are exercised.
/// </summary>
internal partial class HeatingControlService
{
    /// <summary>08:00 on the 1st of every month, local time.</summary>
    internal const string ValveExerciseCron = "0 8 1 * *";

    /// <summary>Fire this Home Assistant event to run the exercise now (tools/ha/valve-exercise.cjs).</summary>
    internal const string ValveExerciseEvent = "home_heating_valve_exercise";

    internal static readonly TimeSpan ValveExerciseFlipFor = TimeSpan.FromSeconds(150);
    internal static readonly TimeSpan ValveExerciseBoilerFor = TimeSpan.FromMinutes(5);

    // Set from the start of an exercise to its end; normal evaluations are skipped meanwhile.
    private bool _valveExerciseRunning;

    // Each exercised valve's setpoint before it was flipped, by entity ID, until it is put back.
    private Dictionary<string, (ICustomClimateControlEntity Valve, double Setpoint)>? _valveExerciseSetpoints;

    // The exercise turned the boiler on, so the first evaluation afterwards turns it off again unless the
    // rooms would have turned it on themselves (see DecideBoilerAsync).
    private bool _boilerOnForExercise;

    private void ScheduleValveExercise()
    {
        _scheduler.ScheduleCron(ValveExerciseCron, async () => await StartValveExerciseAsync("monthly valve exercise"));
        _ha.Events.Filter<object>(ValveExerciseEvent).Subscribe(async _ => await StartValveExerciseAsync($"{ValveExerciseEvent} event"));
    }

    /// <summary>
    /// Starts the exercise: runs the boiler and flips every valve, and schedules putting the valves back and the end.
    /// Returns false when it does not run (the boiler is already on, or an exercise is already running).
    /// </summary>
    internal async Task<bool> StartValveExerciseAsync(string trigger)
    {
        await _evaluationSemaphore.WaitAsync();
        try
        {
            if (_valveExerciseRunning)
            {
                return false;
            }

            BoilerControlPath controlPath = await UpdateBoilerControlPathAsync(trigger);
            if (controlPath == BoilerControlPath.Fingerbot && _boilerOn)
            {
                _logger.LogInformation("Valve exercise skipped: the boiler is already on, so the heating is evidently working. Triggered by {Trigger}", trigger);
                return false;
            }

            RoomSchedules schedules = await LoadSchedulesAsync();
            List<ICustomClimateControlEntity> valves = [.. schedules.Rooms
                .SelectMany(GetSwitchesForRoom)
                .OfType<ICustomClimateControlEntity>()
                .DistinctBy(valve => valve.EntityId)];

            _valveExerciseRunning = true;

            if (controlPath == BoilerControlPath.Fingerbot)
            {
                // A 5-minute burn would fail the turn-on verification, which would then "correct" it with a press.
                _boilerVerification.Reset();
                if (await PressBoilerFingerbotAsync())
                {
                    SetBoilerBelief(true, _timeProvider.GetUtcNow());
                    _boilerOnForExercise = true;
                    _lastBoilerPressWasCorrection = false;
                }
            }

            _valveExerciseSetpoints = [];
            List<string> moved = [];
            foreach (ICustomClimateControlEntity valve in valves)
            {
                if (valve.TargetTemperature is not double setpoint || valve.CurrentTemperature is not double reading)
                {
                    continue;
                }

                bool open = setpoint >= reading;
                _valveExerciseSetpoints[valve.EntityId] = (valve, setpoint);
                valve.SetTargetTemperature(open ? reading - 5 : reading + 5);
                moved.Add($"{valve.EntityId} {(open ? "shut" : "opened")}");
            }

            _logger.LogInformation(
                "Valve exercise started ({Boiler}): {Valves}. Putting them back in {FlipSeconds} s, ending in {EndMinutes} min. Triggered by {Trigger}",
                _boilerOnForExercise ? "boiler on" : controlPath == BoilerControlPath.Fingerbot ? "the boiler could not be pressed" : "boiler left to its thermostat",
                string.Join(", ", moved), ValveExerciseFlipFor.TotalSeconds, ValveExerciseBoilerFor.TotalMinutes, trigger);

            _scheduler.Schedule(ValveExerciseFlipFor, async () => await RestoreValvesAfterExerciseAsync());
            _scheduler.Schedule(ValveExerciseBoilerFor, async () => await EndValveExerciseAsync());

            // Normal evaluations wait for the exercise, so show the boiler going on now rather than in 5 minutes.
            if (_boilerOnForExercise)
            {
                await UpdateRoomStatesAsync();
            }

            return true;
        }
        finally
        {
            _evaluationSemaphore.Release();
        }
    }

    /// <summary>Puts every exercised valve back to the exact setpoint it had before.</summary>
    internal async Task RestoreValvesAfterExerciseAsync()
    {
        await _evaluationSemaphore.WaitAsync();
        try
        {
            RestoreExercisedValves();
        }
        finally
        {
            _evaluationSemaphore.Release();
        }
    }

    private void RestoreExercisedValves()
    {
        if (_valveExerciseSetpoints == null)
        {
            return;
        }

        foreach ((ICustomClimateControlEntity valve, double setpoint) in _valveExerciseSetpoints.Values)
        {
            valve.SetTargetTemperature(setpoint);
        }

        _logger.LogInformation("Valve exercise: {Count} valve(s) put back to their setpoints", _valveExerciseSetpoints.Count);
        _valveExerciseSetpoints = null;
    }

    /// <summary>Ends the exercise and hands back to normal control, which turns the boiler off unless it is needed.</summary>
    internal async Task EndValveExerciseAsync()
    {
        await _evaluationSemaphore.WaitAsync();
        try
        {
            if (!_valveExerciseRunning)
            {
                return;
            }

            // Normally done 2½ minutes in already; nothing guarantees the order of the two scheduled steps.
            RestoreExercisedValves();
            _valveExerciseRunning = false;
            _logger.LogInformation("Valve exercise finished");
        }
        finally
        {
            _evaluationSemaphore.Release();
        }

        await EvaluateAllSchedules("valve exercise finished");
    }
}
