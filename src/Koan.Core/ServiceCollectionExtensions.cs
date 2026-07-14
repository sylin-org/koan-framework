using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Console;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Observability.Health;
using Koan.Core.Modules.Pillars;
using Microsoft.Extensions.Options;
using Koan.Core.Hosting.App;

namespace Koan.Core;

// Core DI bootstrap
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoan(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKoanCore();
        AppBootstrapper.InitializeModules(services);
        return services;
    }

    public static IServiceCollection AddKoanCore(this IServiceCollection services)
    {
        CorePillarManifest.EnsureRegistered();

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

        // Application identity options (centralized metadata shared across modules)
        var identityBuilder = services.AddKoanOptions<ApplicationIdentityOptions>(ApplicationIdentityDefaults.ConfigurationSection);
        identityBuilder.Services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IPostConfigureOptions<ApplicationIdentityOptions>), typeof(ApplicationIdentityPostConfigure)));
        identityBuilder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ApplicationIdentityOptions>>().Value);

        // Bind KoanLog to the ambient logger factory once the host starts up
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, KoanLogFactoryBridge>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupTimelineHostedService>());

        // Best-effort early initialization when provider is built later

        // Health Aggregator (push-first) is the single source of truth for readiness.
        services.AddKoanOptions<Koan.Core.Observability.Health.HealthAggregatorOptions>(Infrastructure.ConfigurationConstants.Health.AggregatorSection);
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Koan.Core.Observability.Health.HealthAggregatorOptions>>().Value);
        services.AddSingleton<Koan.Core.Observability.Health.IHealthAggregator, Koan.Core.Observability.Health.HealthAggregator>();
        // IHealthContributor is the public pull-check seam (implemented by adapters). The registry +
        // bridge fan those contributors into the push-first aggregator on probe.
        services.AddSingleton<IHealthRegistry, HealthRegistry>();
        services.AddHostedService<HealthContributorsBridge>();
        // HealthProbeScheduler is a discovered Koan background service; the background-service
        // orchestrator is its sole execution owner and preserves its pokeable + health aliases.
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

file sealed class StartupTimelineHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;

    public StartupTimelineHostedService(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_lifetime.ApplicationStarted.IsCancellationRequested)
        {
            KoanStartupTimeline.Mark(KoanStartupStage.AppReady);
        }
        else
        {
            _lifetime.ApplicationStarted.Register(() => KoanStartupTimeline.Mark(KoanStartupStage.AppReady));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
