using HomeAssistant.Functions.Models;
using HomeAssistant.Functions.Services;
using HomeAssistant.Services.Climate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HomeAssistant.Functions;

public class ScheduleFunctions
{
    private readonly ILogger<ScheduleFunctions> _logger;
    private readonly ScheduleMapper _mapper;
    private readonly ScheduleStorageService _storageService;

    public ScheduleFunctions(ILogger<ScheduleFunctions> logger)
    {
        _logger = logger;
        _mapper = new ScheduleMapper();

        var connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString")
            ?? throw new InvalidOperationException("ScheduleStorageConnectionString not configured");

        _storageService = new ScheduleStorageService(connectionString);
    }

    [Function("GetSchedules")]
    public async Task<IActionResult> GetSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out var houseIdStr) ||
            !Guid.TryParse(houseIdStr, out var houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        _logger.LogInformation("Getting heating schedules for house {HouseId}", houseId);

        try
        {
            var schedules = await _storageService.GetSchedulesAsync(houseId);

            if (schedules == null)
            {
                // Return default schedules if none exist
                _logger.LogInformation("No schedules found for house {HouseId}, returning defaults", houseId);
                var defaultSchedules = GetDefaultSchedules();
                var defaultResponse = _mapper.ToDto(defaultSchedules);

                // Save defaults for next time
                await _storageService.SaveSchedulesAsync(houseId, defaultResponse);

                return new OkObjectResult(defaultResponse);
            }

            return new OkObjectResult(schedules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("SetSchedules")]
    public async Task<IActionResult> SetSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedules")] HttpRequest req)
    {
        // Get houseId from query parameter
        if (!req.Query.TryGetValue("houseId", out var houseIdStr) ||
            !Guid.TryParse(houseIdStr, out var houseId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing houseId query parameter" });
        }

        _logger.LogInformation("Setting heating schedules for house {HouseId}", houseId);

        try
        {
            using StreamReader reader = new(req.Body);
            var body = await reader.ReadToEndAsync();

            SchedulesResponse? dto = JsonSerializer.Deserialize<SchedulesResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request body" });
            }

            await _storageService.SaveSchedulesAsync(houseId, dto);
            _logger.LogInformation("Successfully saved schedules for house {HouseId} with {RoomCount} rooms",
                houseId, dto.Rooms.Count);

            return new OkObjectResult(new { success = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing JSON for house {HouseId}", houseId);
            return new BadRequestObjectResult(new { error = "Invalid JSON format" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting schedules for house {HouseId}", houseId);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private static List<RoomSchedule> GetDefaultSchedules()
    {
        return
        [
            new RoomSchedule
            {
                Condition = () => true,
                Room = Room.Kitchen,
                ScheduleTracks =
                [
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(5, 30), Temperature = 19, Conditions = ConditionType.PlentyOfPowerAvailable },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(6, 30), Temperature = 18.5 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(6, 30), Temperature = 19, Conditions = ConditionType.PlentyOfPowerAvailable },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(18, 00), Temperature = 19 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21, 30), Temperature = 16 }
                ]
            },
            new RoomSchedule
            {
                Condition = () => true,
                Room = Room.GamesRoom,
                ScheduleTracks =
                [
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(0, 00), Temperature = 14 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(7, 00), Temperature = 18, Days = Days.Weekdays },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(7, 00), Temperature = 19, Conditions = ConditionType.RoomInUse },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(9, 00), Temperature = 16, Conditions = ConditionType.RoomNotInUse },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21, 30), Temperature = 14, Conditions = ConditionType.RoomNotInUse }
                ]
            },
            new RoomSchedule
            {
                Condition = () => true,
                Room = Room.Bedroom1,
                ScheduleTracks =
                [
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(8, 00), Temperature = 19, Days = Days.Weekdays },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(8, 30), Temperature = 16, Days = Days.Weekdays },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(7, 30), Temperature = 19, Days = Days.Saturday },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(8, 00), Temperature = 16, Days = Days.Saturday },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(9, 00), Temperature = 19, Days = Days.Sunday },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(9, 30), Temperature = 16, Days = Days.Sunday },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21, 30), Temperature = 19 },
                    new HeatingScheduleTrack { TargetTime = new TimeOnly(21, 31), Temperature = 14 }
                ]
            }
        ];
    }
}
