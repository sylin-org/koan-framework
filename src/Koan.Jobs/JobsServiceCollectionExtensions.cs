using Koan.Core;
using Koan.Communication;
using Koan.Communication.Signals;
using Koan.Data.Abstractions;
using Koan.Jobs.Semantics;
using Koan.Core.Semantics.Segmentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>DI wiring for the Jobs pillar. Invoked by <see cref="KoanJobsModule"/> (Reference = Intent); also callable
/// directly by tests. Idempotent (TryAdd); ledger election follows the composed Data capabilities.</summary>
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
        services.TryAddSingleton<IJobLedger>(CreateLedger);
        services.TryAddSingleton(_ => JobTypeRegistry.FromDiscovery());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            ISegmentationRealization,
            JobsContextPlan>());
        services.TryAddSingleton(sp => sp
            .GetServices<ISegmentationRealization>()
            .OfType<JobsContextPlan>()
            .Single());
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

    private static IJobLedger CreateLedger(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<JobsOptions>>();
        var registry = services.GetRequiredService<JobTypeRegistry>();
        if (!HasDurableDataAdapter(services))
        {
            var required = registry.All
                .Where(binding => binding.Persistence == JobPersistenceMode.DataStore)
                .Select(binding => binding.WorkType)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (required.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Koan Jobs cannot honor [JobPersistence(DataStore)] for {string.Join(", ", required)} because " +
                    "the host has no durable Data adapter. Reference SQLite, PostgreSQL, SQL Server, MongoDB, or " +
                    "another durable Koan Data provider; otherwise use Auto or InMemory explicitly.");
            }

            return new InMemoryJobLedger(options);
        }

        return new RoutingJobLedger(
            new InMemoryJobLedger(options),
            new DataJobLedger(options, registry),
            registry);
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
