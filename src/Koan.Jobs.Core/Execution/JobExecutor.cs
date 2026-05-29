using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Queue;

namespace Koan.Jobs.Execution;

/// <summary>
/// Thin reflection shim (JOBS-0003): resolves the generic <see cref="JobDispatcher{T}"/> for the
/// queue item's concrete type and runs it. All real execution logic lives in the dispatcher; this
/// only bridges from the non-generic worker loop to the typed dispatcher and supplies the
/// service provider it resolves from.
/// </summary>
internal sealed class JobExecutor
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> Runners = new();
    private readonly IServiceProvider _services;

    public JobExecutor(IServiceProvider services) => _services = services;

    public Task Execute(JobQueueItem item, CancellationToken cancellationToken)
    {
        var runner = Runners.GetOrAdd(item.JobType, static t =>
            typeof(JobDispatcher<>).MakeGenericType(t).GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!);
        return (Task)runner.Invoke(null, new object?[] { item, _services, cancellationToken })!;
    }
}
