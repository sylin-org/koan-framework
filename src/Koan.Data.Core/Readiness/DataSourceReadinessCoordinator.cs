using System.Collections.Concurrent;
using Koan.Data.Abstractions.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Readiness;

/// <summary>
/// Host-owned, stage-specific coalescing for non-mutating reachability, shape validation, and
/// explicitly authorized provisioning. Caller cancellation detaches only that waiter; host shutdown
/// owns cancellation of the shared work.
/// </summary>
public sealed class DataSourceReadinessCoordinator
{
    private readonly CancellationToken _hostStopping;
    private readonly StageState _reachability;
    private readonly StageState _shapeValidation;
    private readonly StageState _provisioning;

    public DataSourceReadinessCoordinator(
        IHostApplicationLifetime? lifetime = null,
        IOptions<DataRuntimeOptions>? options = null)
    {
        _hostStopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
        var capacity = options?.Value.ReadinessCacheEntries ?? 4096;
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                capacity,
                "Data Runtime ReadinessCacheEntries must be greater than zero.");
        _reachability = new StageState(capacity);
        _shapeValidation = new StageState(capacity);
        _provisioning = new StageState(capacity);
    }

    public Task EnsureReachable(
        DataSourcePlan plan,
        string targetIdentity,
        Func<CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Demand(DataOperationEffect.Read, "source reachability");
        return Run(_reachability, Key(plan, targetIdentity), work, ct);
    }

    public Task ValidateShape(
        DataSourcePlan plan,
        string targetIdentity,
        Func<CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Demand(DataOperationEffect.Read, "source shape validation");
        return Run(_shapeValidation, Key(plan, targetIdentity), work, ct);
    }

    public Task Provision(
        DataSourcePlan plan,
        string targetIdentity,
        Func<CancellationToken, Task> provision,
        Func<CancellationToken, Task> validate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(provision);
        ArgumentNullException.ThrowIfNull(validate);
        plan.Demand(DataOperationEffect.SchemaOrAdmin, "source provisioning");
        var key = Key(plan, targetIdentity);
        return Run(_provisioning, key, async hostToken =>
        {
            await provision(hostToken).ConfigureAwait(false);
            _shapeValidation.Completed.TryRemove(key, out _);
            await Run(_shapeValidation, key, validate, hostToken).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>Invalidates structural success after an explicitly authorized shape change.</summary>
    public void InvalidateShape(DataSourcePlan plan, string targetIdentity)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var key = Key(plan, targetIdentity);
        _shapeValidation.Completed.TryRemove(key, out _);
        _provisioning.Completed.TryRemove(key, out _);
    }

    private async Task Run(
        StageState stage,
        string key,
        Func<CancellationToken, Task> work,
        CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        callerToken.ThrowIfCancellationRequested();
        if (stage.IsComplete(key)) return;

        var lazy = stage.Inflight.GetOrAdd(
            key,
            _ => new Lazy<Task>(
                () => RunShared(stage, key, work),
                LazyThreadSafetyMode.ExecutionAndPublication));

        await lazy.Value.WaitAsync(callerToken).ConfigureAwait(false);
    }

    private async Task RunShared(StageState stage, string key, Func<CancellationToken, Task> work)
    {
        try
        {
            await work(_hostStopping).ConfigureAwait(false);
            stage.RememberComplete(key);
        }
        finally
        {
            stage.Inflight.TryRemove(key, out _);
        }
    }

    private static string Key(DataSourcePlan plan, string targetIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIdentity);
        return $"{plan.RouteIdentity}:{targetIdentity}";
    }

    private sealed class StageState(int capacity)
    {
        private long _stamp;
        public ConcurrentDictionary<string, Lazy<Task>> Inflight { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, long> Completed { get; } = new(StringComparer.Ordinal);

        public bool IsComplete(string key) => Completed.ContainsKey(key);

        public void RememberComplete(string key)
        {
            Completed[key] = Interlocked.Increment(ref _stamp);
            var overflow = Completed.Count - capacity;
            if (overflow <= 0) return;

            foreach (var oldest in Completed.OrderBy(static pair => pair.Value).Take(overflow))
                ((ICollection<KeyValuePair<string, long>>)Completed).Remove(oldest);
        }
    }
}
