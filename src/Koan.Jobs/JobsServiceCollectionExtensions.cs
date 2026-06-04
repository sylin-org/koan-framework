using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services.TryAddSingleton<IJobLedger, InMemoryJobLedger>();
        services.TryAddSingleton(_ => JobTypeRegistry.FromDiscovery());
        services.TryAddSingleton<JobOrchestrator>();
        services.TryAddSingleton<JobScheduler>();
        services.TryAddSingleton<IJobCoordinator, JobCoordinator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobWorkerService>());
        return services;
    }
}
