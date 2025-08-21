using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sora.Core;

// Core DI bootstrap
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraCore(this IServiceCollection services)
    {
        // Default logging: simple console with concise output and sane category levels.
        services.AddLogging(logging =>
        {
            logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            logging.AddFilter("Sora", LogLevel.Information);
        });

        // Best-effort early initialization when provider is built later

        // Legacy health registry removed in greenfield; aggregator is the single source of truth
        // Health Aggregator (push-first)
        services.AddOptions<HealthAggregatorOptions>().BindConfiguration("Sora:Health:Aggregator");
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthAggregatorOptions>>().Value);
        services.AddSingleton<IHealthAggregator, HealthAggregator>();
        services.AddHostedService<HealthAggregatorScheduler>();
        return services;
    }
}
