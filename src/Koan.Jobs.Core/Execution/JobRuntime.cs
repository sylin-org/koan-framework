using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Jobs.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Execution;

/// <summary>
/// Static seam the public <see cref="Job{T}"/> instance API (Submit/Cancel) delegates to (JOBS-0003).
/// Resolves the <see cref="IJobCoordinator"/> from the running host so jobs stay POCOs with no
/// injected services.
/// </summary>
internal static class JobRuntime
{
    private static IJobCoordinator Coordinator =>
        (AppHost.Current ?? throw new InvalidOperationException(
            "AppHost.Current is not set. Ensure services.AddKoan() has run and the host is started."))
        .GetRequiredService<IJobCoordinator>();

    public static Task<T> Submit<T>(T job, TimeSpan? delay, CancellationToken cancellationToken)
        where T : Job<T>, new()
        => Coordinator.Submit(job, delay, cancellationToken);

    public static Task Cancel<T>(string jobId, CancellationToken cancellationToken)
        where T : Job<T>, new()
        => Coordinator.Cancel<T>(jobId, cancellationToken);
}
