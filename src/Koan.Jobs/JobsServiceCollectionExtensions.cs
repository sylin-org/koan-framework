using Koan.Core;
using Koan.Communication;
using Koan.Communication.Signals;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>DI wiring for the Jobs pillar. Invoked by <see cref="KoanJobsModule"/> (Reference = Intent); also callable
/// directly by tests. Idempotent (TryAdd). Phase 0 registers the in-memory ledger; the durable election is wired later.</summary>
public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddKoanJobs(this IServiceCollection services, Action<JobsOptions>? configure = null)
    {
        // Jobs owns the wake meaning; Communication owns its local/network carriage and provider election.
        services.AddKoanCommunication();
        services.AddOptions();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        // Capability election. No durable adapter → in-memory only (Local tier). Durable adapter present →
        // RoutingJobLedger so [JobPersistence(InMemory)] types stay volatile while Auto/DataStore types persist.
        services.TryAddSingleton<IJobLedger>(sp => HasDurableDataAdapter(sp)
            ? new RoutingJobLedger(
                new InMemoryJobLedger(sp.GetRequiredService<IOptions<JobsOptions>>()),
                new DataJobLedger(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IOptions<JobsOptions>>(), sp.GetRequiredService<JobTypeRegistry>()),
                sp.GetRequiredService<JobTypeRegistry>())
            : new InMemoryJobLedger(sp.GetRequiredService<IOptions<JobsOptions>>()));
        services.TryAddSingleton(_ => JobTypeRegistry.FromDiscovery());
        services.TryAddSingleton<JobWakeCoordinator>();
        services.AddFrameworkSignal<JobReadySignal, JobWakeCoordinator>();
        services.TryAddSingleton<JobOrchestrator>();
        services.TryAddSingleton<JobScheduler>();
        services.TryAddSingleton<IJobCoordinator, JobCoordinator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobWorkerService>());
        // Self-reporting (JOBS-0008): per-lane queue depth / oldest-queued-age / reclaim backlog → /health, so a stalled
        // or starved lane is a first-class signal instead of an inference. Mirrors how the data connectors register theirs.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JobsHealthContributor>());
        return services;
    }

    /// <summary>Register a custom pool resolver so the orchestrator knows how to dispatch pool jobs (JOBS-0007).
    /// Multiple resolvers may be registered; each handles a distinct pool name.</summary>
    public static IServiceCollection AddJobPoolResolver<T>(this IServiceCollection services)
        where T : class, IJobPoolResolver
    {
        services.AddSingleton<IJobPoolResolver, T>();
        return services;
    }

    /// <summary>True when a durable data adapter (Mongo/Postgres/SqlServer/SQLite/…) is registered — i.e. anything
    /// other than the in-memory / JSON development adapters. Gates the durable-ledger election.</summary>
    private static bool HasDurableDataAdapter(IServiceProvider sp)
    {
        foreach (var factory in sp.GetServices<IDataAdapterFactory>())
        {
            var name = factory.GetType().Name;
            if (name.IndexOf("InMemory", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Json", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
        }
        return false;
    }
}
