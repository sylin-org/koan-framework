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
        services.AddOptions();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        // Capability election: a durable data adapter present → data-backed ledger; else in-memory (Local tier).
        services.TryAddSingleton<IJobLedger>(sp => HasDurableDataAdapter(sp)
            ? new DataJobLedger(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IOptions<JobsOptions>>())
            : new InMemoryJobLedger());
        services.TryAddSingleton(_ => JobTypeRegistry.FromDiscovery());
        services.TryAddSingleton<JobOrchestrator>();
        services.TryAddSingleton<JobScheduler>();
        services.TryAddSingleton<IJobCoordinator, JobCoordinator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobWorkerService>());
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
