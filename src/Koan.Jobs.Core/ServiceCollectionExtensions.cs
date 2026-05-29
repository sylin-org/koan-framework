using Koan.Core.Modules;
using Koan.Jobs.Archival;
using Koan.Jobs.Infrastructure;
using Koan.Jobs.Execution;
using Koan.Jobs.Events;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;
using Koan.Jobs.Queue;
using Koan.Jobs.RateGating;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Jobs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanJobs(this IServiceCollection services)
    {
        services.AddKoanOptions<JobsOptions>(ConfigurationConstants.Section);
        services.TryAddSingleton<JobProgressBroker>();
        services.TryAddSingleton<JobCancellations>();
        services.TryAddSingleton<JobTypeRegistry>();
        services.TryAddSingleton<IJobQueue, InMemoryJobQueue>();
        services.TryAddSingleton<IHostRateGate, InMemoryHostRateGate>();
        services.TryAddSingleton<JobLaneRegistry>();
        services.TryAddSingleton<IJobCoordinator, JobCoordinator>();
        services.TryAddSingleton<JobExecutor>();
        services.TryAddSingleton<IJobEventPublisher, JobEventPublisher>();

        services.AddHostedService<JobWorkerService>();
        services.AddHostedService<JobRecoveryService>();
        services.AddHostedService<JobArchivalService>();
        return services;
    }
}
