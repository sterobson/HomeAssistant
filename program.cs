using HomeAssistant.apps.Energy;
using HomeAssistant.apps.Energy.Octopus;
using HomeAssistant.apps.Energy.Solax;
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
        .ConfigureServices((context, services) =>
        {
            services
                .AddAppsFromAssembly(Assembly.GetExecutingAssembly())
                .AddNetDaemonStateManager()
                .AddNetDaemonScheduler()
                .AddHomeAssistantGenerated()
                .AddConfiguration<OctopusConfiguration>(context, "Octopus")
                .AddConfiguration<SolaxConfiguration>(context, "Solax")
                .AddSingleton<IElectricityRatesReader, OctopusReader>();
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

static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguration<T>(this IServiceCollection services, HostBuilderContext context, string configSection) where T : class, new()
    {
        T config = new();
        context.Configuration.GetSection(configSection).Bind(config);
        return services.AddSingleton<T>(config);
    }
}