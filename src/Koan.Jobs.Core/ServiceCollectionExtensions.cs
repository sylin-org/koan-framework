using Koan.Core.Modules;
using Koan.Jobs.Execution;
using Koan.Jobs.Events;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;
using Koan.Jobs.Queue;
using Koan.Jobs.Store;
using Koan.Jobs.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Jobs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanJobs(this IServiceCollection services)
    {
        services.AddKoanOptions<JobsOptions>("Koan:Jobs");
        services.TryAddSingleton<JobIndexCache>();
        services.TryAddSingleton<JobProgressBroker>();
        services.TryAddSingleton<InMemoryJobStore>();
        services.TryAddSingleton<EntityJobStore>();
        services.TryAddSingleton<IJobStoreResolver, JobStoreResolver>();
        services.TryAddSingleton<IJobQueue, InMemoryJobQueue>();
        services.TryAddSingleton<IJobCoordinator, JobCoordinator>();
        services.TryAddSingleton<JobExecutor>();
        services.TryAddSingleton<IJobEventPublisher, JobEventPublisher>();

        services.AddHostedService<JobWorkerService>();
        services.AddHostedService<InMemoryJobSweeper>();
        return services;
    }
}
