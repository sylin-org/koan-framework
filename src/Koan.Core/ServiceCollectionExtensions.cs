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
using Koan.Core.Context;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Core;

// Core DI bootstrap
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoan(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Compose(services, configure: null);
    }

    /// <summary>
    /// Adds Koan and declares host-owned capability behavior in the same composition.
    /// </summary>
    /// <remarks>
    /// The parameterless overload remains the complete zero-configuration bootstrap. Use this
    /// overload only when the application has business-specific declarations such as
    /// <c>Order.Lifecycle.BeforeUpsert(...)</c>.
    /// </remarks>
    public static IServiceCollection AddKoan(this IServiceCollection services, Action configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return Compose(services, configure);
    }

    private static IServiceCollection Compose(IServiceCollection services, Action? configure)
    {
        var session = SemanticCompositionSession.GetOrCreate(services);
        if (session.IsFrozen)
        {
            if (configure is null) return services;
            throw new InvalidOperationException(
                "This Koan application is already composed. Put business declarations in the initial AddKoan(() => ...) call.");
        }

        using var lease = session.Enter();
        try
        {
            using var declarationScope = Composition.KoanCompositionScope.Enter(services);
            if (session.TryConfigureCore()) services.AddKoanCore();
            AppBootstrapper.InitializeModules(services);
            configure?.Invoke();
            lease.Complete();
            return services;
        }
        catch (Exception exception)
        {
            session.FailComposition(exception);
            throw;
        }
    }

    public static IServiceCollection AddKoanCore(this IServiceCollection services)
    {
        CorePillarManifest.EnsureRegistered();

        var segmentation = new SegmentationPlanBuilder();
        SemanticCompositionSession.GetOrCreate(services).ScheduleContributions<
            SegmentationContributionTarget,
            SegmentationPlan>(
            segmentation.ForOwner,
            segmentation.Build,
            static (collection, plan) => collection.Replace(ServiceDescriptor.Singleton(plan)));
        services.TryAddSingleton(SegmentationPlan.Empty);

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
        identityBuilder.Services.TryAdd(ServiceDescriptor.Singleton(
            typeof(ApplicationIdentitySnapshot),
            sp => sp.GetRequiredService<ApplicationIdentityOptions>().ToSnapshot()));

        // R07-01: one host-owned registry carries module-owned logical-flow context across durable boundaries.
        // The ambient KoanContext itself is flow-local and static; carrier instances and their composition stay in DI.
        services.TryAddSingleton<KoanContextCarrierRegistry>();
        services.TryAddSingleton<SegmentationContextPlan>();

        // Establish the canonical ambient host before other hosted services start. KoanLog and other
        // terse framework surfaces resolve host-owned services through this same owner.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AppHostBinderHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupTimelineHostedService>());

        // One host-owned explanation snapshot feeds startup, health, operators, and agents.
        services.TryAddSingleton<Koan.Core.Diagnostics.KoanRuntimeFactStore>();
        services.TryAddSingleton<Koan.Core.Diagnostics.IKoanRuntimeFacts>(sp =>
            sp.GetRequiredService<Koan.Core.Diagnostics.KoanRuntimeFactStore>());
        services.TryAddSingleton<Koan.Core.Diagnostics.IKoanRuntimeFactRecorder>(sp =>
            sp.GetRequiredService<Koan.Core.Diagnostics.KoanRuntimeFactStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, Koan.Core.Diagnostics.KoanFactsHealthContributor>());

        if (!services.Any(d => d.ServiceType == typeof(Koan.Core.Hosting.Runtime.IAppRuntime)))
            services.AddSingleton<Koan.Core.Hosting.Runtime.IAppRuntime, Koan.Core.Hosting.Runtime.AppRuntime>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Koan.Core.Hosting.Runtime.AppRuntimeHostedService>());

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
        Orchestration.ServiceDiscoveryBootstrap.Register(services);
        BackgroundServices.KoanBackgroundServicesBootstrap.Register(services);
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
