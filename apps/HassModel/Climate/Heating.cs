using HomeAssistant.Services;
using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading.Tasks;

namespace HomeAssistant.apps.HassModel.Climate;

[NetDaemonApp]
internal class Heating
{
    private readonly HistoryService _historyService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<DiningRoomDehumidifier> _logger;
    private readonly MyDevices _myDevices;
    private const int _minutesOfNoPowerUseBeforeNotification = 10;
    private const int _minutesToWaitBeforeNextStateChange = 5;
    private readonly DateTime _lastStateChangeTime = DateTime.MinValue;

    public Heating(IHaContext ha, HistoryService historyService, IScheduler scheduler,
        NotificationService notificationService, ILogger<DiningRoomDehumidifier> logger)
    {
        Entities entities = new(ha);
        _myDevices = new(entities, ha);
        _historyService = historyService;
        _notificationService = notificationService;
        _logger = logger;

        Schedule kitchenSchedule = new()
        {
            Condition = () => true,
            GetCurrentTemperature = () => _myDevices.KitchenTemperature.State ?? 20,
            SetCurrentState = (value) => Task.CompletedTask,
            Temperatures = [
                new TargetTemperature{ Temperature = 19, TargetTime = new TimeOnly(6,30)},
                new TargetTemperature{ Temperature = 20, TargetTime = new TimeOnly(18,00)},
                new TargetTemperature{ Temperature = 18, TargetTime = new TimeOnly(19,00)},
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(21,30)}
            ]
        };

        Schedule gamesRoomSchedule = new()
        {
            Condition = () => true,
            GetCurrentTemperature = () => _myDevices.GamesRoomDeskTemperature.State ?? 20,
            SetCurrentState = (value) => Task.CompletedTask,
            Temperatures = [
                new TargetTemperature{ Temperature = 18, TargetTime = new TimeOnly(7,00), Days = Days.Weekdays},
                new TargetTemperature{ Temperature = 18, TargetTime = new TimeOnly(9,00), Condition = () => true }, // Only if the desk has been on and in use
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(9,00), Condition = () => false }, // Only if the desk has not been in use
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(18,00)}, // Always cool down by this time
                new TargetTemperature{ Temperature = 14, TargetTime = new TimeOnly(21,30)}
            ]
        };

        Schedule beddroomSchedule = new()
        {
            Condition = () => true,
            GetCurrentTemperature = () => _myDevices.Bedroom1Temperature.State ?? 20,
            SetCurrentState = (value) => Task.CompletedTask,
            Temperatures = [
                new TargetTemperature{ Temperature = 19, TargetTime = new TimeOnly(8,00), Days = Days.Weekdays},
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(8,30), Days = Days.Weekdays},
                new TargetTemperature{ Temperature = 19, TargetTime = new TimeOnly(7,30), Days = Days.Saturday},
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(7,30), Days = Days.Saturday},
                new TargetTemperature{ Temperature = 19, TargetTime = new TimeOnly(9,00), Days = Days.Sunday},
                new TargetTemperature{ Temperature = 16, TargetTime = new TimeOnly(9,30), Days = Days.Sunday},
                new TargetTemperature{ Temperature = 19, TargetTime = new TimeOnly(21,30)},
                new TargetTemperature{ Temperature = 14, TargetTime = new TimeOnly(22,00)},
            ]
        };

    }

    private class Schedule
    {
        public required Func<bool> Condition { get; set; }
        public required Func<double> GetCurrentTemperature { get; set; }
        public required Func<bool, Task> SetCurrentState { get; set; }
        public required List<TargetTemperature> Temperatures { get; set; }
    }

    [Flags]
    private enum Days
    {
        Everyday = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 4,
        Thursday = 8,
        Friday = 16,
        Saturday = 32,
        Sunday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekends = Saturday | Sunday
    }

    private class TargetTemperature
    {
        public required double Temperature { get; set; }
        public required TimeOnly TargetTime { get; set; }
        public int RampUpMinutes { get; set; } = 30;
        public Days Days { get; set; } = Days.Everyday;
        public Func<bool>? Condition { get; set; }
    }
}
