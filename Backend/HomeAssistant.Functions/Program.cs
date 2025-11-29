using HomeAssistant.Functions.JsonConverters;
using HomeAssistant.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Configure JSON serialization options to handle enums as both numbers and strings
builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.Converters.Add(new FlexibleEnumConverterFactory());
});

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights()
    .AddSingleton<ScheduleStorageService>(serviceProvider =>
    {
        string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString") ?? string.Empty;
        return new ScheduleStorageService(connectionString);
    })
    .AddSingleton<RoomStateStorageService>(serviceProvider =>
     {
         string connectionString = Environment.GetEnvironmentVariable("ScheduleStorageConnectionString") ?? string.Empty;
         return new RoomStateStorageService(connectionString);
     });

builder.Build().Run();
