using HomeAssistant;
using HomeAssistant.apps;
using HomeAssistant.apps.Energy;
using HomeAssistant.apps.Energy.Octopus;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
using HomeAssistant.Services.Climate;
using HomeAssistant.Services.WasteManagement;
using HomeAssistant.Weather;
using HomeAssistant.Weather.WeatherApi;
using HomeAssistantGenerated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetDaemon.Extensions.Logging;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.Extensions.Tts;
using NetDaemon.Runtime;
using System.Reflection;

#pragma warning disable CA1812

try
{
    await Host.CreateDefaultBuilder(args)
        .UseNetDaemonAppSettings()
        .UseNetDaemonDefaultLogging()
        .UseNetDaemonRuntime()
        .UseNetDaemonTextToSpeech()
        .ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.secrets.all.json", optional: true, reloadOnChange: true);
            config.AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true);
            config.AddJsonFile("appsettings.secrets.development.json", optional: true, reloadOnChange: true);
            config.AddJsonFile("appsettings.production.json", optional: true, reloadOnChange: true);
            config.AddJsonFile("appsettings.secrets.production.json", optional: true, reloadOnChange: true);
        })
        .ConfigureServices((context, services) =>
        {
            services
                .AddAppsFromAssembly(Assembly.GetExecutingAssembly())
                .AddNetDaemonStateManager()
                .AddNetDaemonScheduler()
                .AddHomeAssistantGenerated()
                .AddConfiguration<OctopusConfiguration>(context, "Octopus")
                .AddScoped<IElectricityRatesReader, OctopusReader>()
                .AddScoped<IElectricityMeter, OctopusElectricityMeter>()
                .AddScoped<OctopusElectricityMeter>()
                .AddScoped<SolaxInverter>()
                .AddScoped<IHomeBattery>(provider => provider.GetRequiredService<SolaxInverter>())
                .AddScoped<ISolarPanels>(provider => provider.GetRequiredService<SolaxInverter>())
                .AddScoped<ICarCharger, HypervoltPro3>()
                .AddScoped<NotificationService>()
                .AddConfiguration<WeatherApiConfiguration>(context, "WeatherApi")
                .AddSingleton<IWeatherProvider, WeatherApiProvider>()
                .AddSingleton<LocationProvider>()
                .AddConfiguration<HomeAssistantConfiguration>(context, "HomeAssistantApiEndpoints")
                .AddConfiguration<WebSynchronisationConfiguration>(context, "WebSynchronisation")
                .AddSingleton<HistoryService>()
                .AddConfiguration<YorkBinServiceConfiguration>(context, "YorkBinService")
                .AddSingleton<IWasteCollectionService, YorkWasteCollectionService>()
                .AddScoped<NamedEntities>()
                .AddScoped<INamedEntities>(provider => provider.GetRequiredService<NamedEntities>())
                .AddScoped<IPresenceService, PresenceService>()
                .AddHttpClient<IScheduleApiClient, ScheduleApiClient>((serviceProvider, client) =>
                {
                    WebSynchronisationConfiguration config = serviceProvider.GetRequiredService<WebSynchronisationConfiguration>();
                    if (!string.IsNullOrEmpty(config.ScheduleApiUrl))
                    {
                        client.BaseAddress = new Uri(config.ScheduleApiUrl);
                    }
                })
                .Services
                .AddScoped<ISchedulePersistenceService, SchedulePersistenceService>()
                .AddScoped<IRoomStatePersistenceService, RoomStatePersistenceService>()
                .AddScoped<HeatingControlService>()
                .AddSingleton<TimeProvider>(provider => TimeProvider.System);

        })
        .Build()
        .RunAsync()
        .ConfigureAwait(false);
}
catch (Exception e)
{
    Console.WriteLine($"Failed to start host... {e}");
    throw;
}

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguration<T>(this IServiceCollection services, HostBuilderContext context, string configSection) where T : class, new()
    {
        T config = new();
        context.Configuration.GetSection(configSection).Bind(config);
        return services.AddSingleton<T>(config);
    }
}