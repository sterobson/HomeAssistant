using HomeAssistant;
using HomeAssistant.apps.Energy;
using HomeAssistant.apps.Energy.Octopus;
using HomeAssistant.Devices.Batteries;
using HomeAssistant.Devices.CarChargers;
using HomeAssistant.Devices.Meters;
using HomeAssistant.Services;
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
using System.IO;
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
            if (File.Exists("appsettings.development.json"))
            {
                config.AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true);
            }
            else
            {
                config.AddJsonFile("appsettings.production.json", optional: true, reloadOnChange: true);
            }
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
                .AddSingleton<HistoryService>();
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