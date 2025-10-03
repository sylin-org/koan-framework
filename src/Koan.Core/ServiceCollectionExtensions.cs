using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Observability.Health;

namespace Koan.Core;

// Core DI bootstrap
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanCore(this IServiceCollection services)
    {
        // Default logging: simple console with concise output and sane category levels.
        services.AddLogging(logging =>
        {
            logging.AddConsole(options =>
            {
                options.FormatterName = KoanConsoleFormatter.FormatterName;
            });
            logging.AddConsoleFormatter<KoanConsoleFormatter, KoanConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "HH:mm:ss";
                options.IncludeScopes = false;
                options.UseUtcTimestamp = false;
                options.IncludeCategory = true;
                options.CategoryMode = KoanConsoleCategoryMode.Short;
                options.IncludeSourceSuffix = true;
            });

            // Default verbosity: Debug outside Production for better diagnostics
            logging.SetMinimumLevel(KoanEnv.IsProduction ? LogLevel.Information : LogLevel.Debug);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
            logging.AddFilter("Koan", KoanEnv.IsProduction ? LogLevel.Information : LogLevel.Debug);
        });

        // Bind KoanLog to the ambient logger factory once the host starts up
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, KoanLogFactoryBridge>());

        // Best-effort early initialization when provider is built later

        // Legacy health registry removed in greenfield; aggregator is the single source of truth
        // Health Aggregator (push-first)
        // Note: Fully qualify Options type to avoid collision with Koan.Core.HealthAggregatorOptions.
        services.AddKoanOptions<Koan.Core.Observability.Health.HealthAggregatorOptions>("Koan:Health:Aggregator");
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Koan.Core.Observability.Health.HealthAggregatorOptions>>().Value);
        services.AddSingleton<Koan.Core.Observability.Health.IHealthAggregator, Koan.Core.Observability.Health.HealthAggregator>();
        // Bridge legacy contributors (registered by adapters) into aggregator
        services.AddSingleton<IHealthRegistry, HealthRegistry>();
        services.AddHostedService<HealthContributorsBridge>();
        services.AddHostedService<HealthProbeScheduler>();
        // Kick a startup probe so readiness is populated early
        services.AddHostedService<StartupProbeService>();
        // Hosting runtime: apps depend on greenfield IAppRuntime
        if (!services.Any(d => d.ServiceType == typeof(Koan.Core.Hosting.Runtime.IAppRuntime)))
            services.AddSingleton<Koan.Core.Hosting.Runtime.IAppRuntime, Koan.Core.Hosting.Runtime.AppRuntime>();

        // Ensure ambient host is set in generic hosts (web apps do this via startup filter)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, Koan.Core.Hosting.App.AppHostBinderHostedService>());
        return services;
    }
}