using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sora.Core.Modules;
using Sora.Core.Observability.Health;

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
            // Default verbosity: Debug outside Production for better diagnostics
            logging.SetMinimumLevel(SoraEnv.IsProduction ? LogLevel.Information : LogLevel.Debug);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            logging.AddFilter("Sora", SoraEnv.IsProduction ? LogLevel.Information : LogLevel.Debug);
        });

        // Best-effort early initialization when provider is built later

        // Legacy health registry removed in greenfield; aggregator is the single source of truth
        // Health Aggregator (push-first)
        // Note: Fully qualify Options type to avoid collision with Sora.Core.HealthAggregatorOptions.
        services.AddSoraOptions<Sora.Core.Observability.Health.HealthAggregatorOptions>("Sora:Health:Aggregator");
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Sora.Core.Observability.Health.HealthAggregatorOptions>>().Value);
        services.AddSingleton<Sora.Core.Observability.Health.IHealthAggregator, Sora.Core.Observability.Health.HealthAggregator>();
        // Bridge legacy contributors (registered by adapters) into aggregator
        services.AddSingleton<IHealthRegistry, HealthRegistry>();
        services.AddHostedService<HealthContributorsBridge>();
        services.AddHostedService<HealthProbeScheduler>();
        // Kick a startup probe so readiness is populated early
        services.AddHostedService<StartupProbeService>();
        // Hosting runtime: apps depend on greenfield IAppRuntime
        if (!services.Any(d => d.ServiceType == typeof(Sora.Core.Hosting.Runtime.IAppRuntime)))
            services.AddSingleton<Sora.Core.Hosting.Runtime.IAppRuntime, Sora.Core.Hosting.Runtime.AppRuntime>();
        return services;
    }
}