using System;
using System.Threading;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Jobs.Execution;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;
using Koan.Jobs.Model;

namespace Koan.Jobs.Support;

internal static class JobEnvironment
{
    private static IServiceProvider ServiceProvider
        => AppHost.Current ?? throw new InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() has been executed and the host is running.");

    internal static JobsOptions Options => ServiceProvider.GetRequiredService<IOptions<JobsOptions>>().Value;

    internal static JobProgressBroker ProgressBroker => ServiceProvider.GetRequiredService<JobProgressBroker>();

    internal static IJobCoordinator Coordinator => ServiceProvider.GetRequiredService<IJobCoordinator>();

    internal static JobRunBuilder<TJob, TContext, TResult> CreateBuilder<TJob, TContext, TResult>(
        TContext context,
        string? correlationId,
        CancellationToken cancellationToken)
        where TJob : Job<TJob, TContext, TResult>, new()
    {
        return new JobRunBuilder<TJob, TContext, TResult>(
            ServiceProvider,
            typeof(TJob),
            context,
            correlationId,
            cancellationToken);
    }
}
