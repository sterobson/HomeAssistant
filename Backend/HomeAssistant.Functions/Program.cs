using HomeAssistant.Functions;
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
// and use camelCase property naming for JavaScript frontend compatibility
builder.Services.Configure<JsonSerializerOptions>(JsonConfiguration.ConfigureOptions);

// Also configure ASP.NET Core JSON options for HTTP responses
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    JsonConfiguration.ConfigureOptions(options.SerializerOptions);
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
     })
    .AddSingleton<SignalRService>();

builder.Build().Run();
