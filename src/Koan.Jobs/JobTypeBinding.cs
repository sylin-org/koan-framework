using System.Reflection;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// Everything the orchestrator needs about one work-item type, bound <b>once at bootstrap</b> from the discovered
/// <see cref="IKoanJob"/> implementors — load/save/execute delegates (no per-dispatch reflection), the chain,
/// per-action policy, persistence mode, and the coalesce/gate accessors. JOBS-0005 §4.1.
/// </summary>
public sealed class JobTypeBinding
{
    private readonly IReadOnlyDictionary<string, JobActionAttribute> _actions;
    private readonly PropertyInfo[] _coalesceProps;
    private readonly PropertyInfo? _gateProp;

    internal JobTypeBinding(
        string workType,
        Type clrType,
        Func<string, CancellationToken, Task<object?>> load,
        Func<object, CancellationToken, Task> save,
        Func<object, JobContext, CancellationToken, Task> execute,
        Func<object, string> getId,
        Func<string, object> newSingleton,
        string[] chain,
        JobPersistenceMode persistence,
        string? pinnedProvider,
        bool parallelSafe,
        IReadOnlyDictionary<string, JobActionAttribute> actions,
        PropertyInfo[] coalesceProps,
        PropertyInfo? gateProp)
    {
        WorkType = workType;
        ClrType = clrType;
        Load = load;
        Save = save;
        Execute = execute;
        GetId = getId;
        NewSingleton = newSingleton;
        Chain = chain;
        Persistence = persistence;
        PinnedProvider = pinnedProvider;
        ParallelSafe = parallelSafe;
        _actions = actions;
        _coalesceProps = coalesceProps;
        _gateProp = gateProp;
    }

    /// <summary>Stable type key (the work-item type's full name); stored on each <see cref="JobRecord.WorkType"/>.</summary>
    public string WorkType { get; }
    public Type ClrType { get; }
    public Func<string, CancellationToken, Task<object?>> Load { get; }
    public Func<object, CancellationToken, Task> Save { get; }
    public Func<object, JobContext, CancellationToken, Task> Execute { get; }
    public Func<object, string> GetId { get; }
    /// <summary>Create a fresh work-item instance with a stable id — used for type-level triggers and scheduled
    /// singleton ticks, which have no caller-supplied instance.</summary>
    public Func<string, object> NewSingleton { get; }
    public string[] Chain { get; }
    public JobPersistenceMode Persistence { get; }
    public string? PinnedProvider { get; }

    /// <summary>True when the type opts out of per-entity serialization (<c>[ParallelSafe]</c>): its actions may
    /// run concurrently on one instance. Default false — jobs for the same instance are serialized (JOBS-0005 §17.2).</summary>
    public bool ParallelSafe { get; }

    /// <summary>The next stage after <paramref name="action"/> in the declared <c>[JobChain]</c>, or null (terminal / not chained).</summary>
    public string? NextInChain(string action)
    {
        for (var i = 0; i < Chain.Length - 1; i++)
            if (string.Equals(Chain[i], action, StringComparison.Ordinal))
                return Chain[i + 1];
        return null;
    }

    /// <summary>Resolve the effective policy for an action (attribute values, else option defaults).</summary>
    public ResolvedActionPolicy ResolvePolicy(string action, JobsOptions o)
    {
        _actions.TryGetValue(action, out var a);
        var lane = !string.IsNullOrWhiteSpace(a?.Lane) ? a!.Lane!
                 : string.IsNullOrEmpty(action) ? "default" : action;
        var maxAttempts = a is { MaxAttempts: >= 0 } ? a.MaxAttempts : o.DefaultMaxAttempts;
        var maxConc = a is { MaxConcurrency: >= 0 } ? a.MaxConcurrency : o.DefaultMaxConcurrency;
        var onFailure = a?.OnFailure ?? OnFailure.Abort;
        var timeout = ParseSpan(a?.Timeout);
        var deadline = ParseSpan(a?.Deadline) ?? o.DefaultDeadline;
        var maxResched = a is { MaxReschedules: >= 0 } ? a.MaxReschedules : o.DefaultMaxReschedules;
        return new ResolvedActionPolicy(action, lane, maxAttempts, Math.Max(1, maxConc), onFailure, timeout, a?.Schedule, deadline, maxResched);
    }

    /// <summary>Actions declared with a <c>Schedule</c> (level-triggered reconcile sweeps).</summary>
    public IEnumerable<ResolvedActionPolicy> ScheduledActions(JobsOptions o)
        => _actions.Values.Where(a => !string.IsNullOrWhiteSpace(a.Schedule)).Select(a => ResolvePolicy(a.Action, o));

    /// <summary>Compute the coalesce/idempotency key for a work-item + action, or null if the type has no <c>[JobIdempotent]</c>.</summary>
    public string? CoalesceKey(object workItem, string action)
    {
        if (_coalesceProps.Length == 0) return null;
        var parts = new string[_coalesceProps.Length + 2];
        parts[0] = WorkType;
        parts[1] = action;
        for (var i = 0; i < _coalesceProps.Length; i++)
            parts[i + 2] = _coalesceProps[i].GetValue(workItem)?.ToString() ?? "";
        return string.Join('|', parts);
    }

    /// <summary>The gate key a work-item contends for (from <c>[JobGate(prop)]</c>), or null.</summary>
    public string? GateKey(object workItem) => _gateProp?.GetValue(workItem)?.ToString();

    private static TimeSpan? ParseSpan(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : TimeSpan.TryParse(s, out var t) ? t : null;
}

/// <summary>Binds a discovered work-item type into a <see cref="JobTypeBinding"/> — invoked once per type at bootstrap.</summary>
internal static class JobTypeBinder
{
    private const BindingFlags PropFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    public static JobTypeBinding Bind(Type workType)
    {
        var m = typeof(JobTypeBinder).GetMethod(nameof(BindGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(workType);
        return (JobTypeBinding)m.Invoke(null, null)!;
    }

    private static JobTypeBinding BindGeneric<T>() where T : Entity<T>, IKoanJob<T>
    {
        var clr = typeof(T);
        var actions = clr.GetCustomAttributes<JobActionAttribute>(inherit: true)
            .GroupBy(a => a.Action, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var chain = clr.GetCustomAttribute<JobChainAttribute>(inherit: true)?.Stages ?? Array.Empty<string>();
        var idem = clr.GetCustomAttribute<JobIdempotentAttribute>(inherit: true);
        var gate = clr.GetCustomAttribute<JobGateAttribute>(inherit: true);
        var persist = clr.GetCustomAttribute<JobPersistenceAttribute>(inherit: true);
        var parallelSafe = clr.GetCustomAttribute<ParallelSafeAttribute>(inherit: true) is not null;

        var coalesceProps = (idem?.Keys ?? Array.Empty<string>())
            .Select(k => clr.GetProperty(k, PropFlags)
                ?? throw new InvalidOperationException($"[JobIdempotent] key '{k}' is not a public property of {clr.Name}."))
            .ToArray();
        var gateProp = gate is null ? null
            : clr.GetProperty(gate.Property, PropFlags)
              ?? throw new InvalidOperationException($"[JobGate] property '{gate.Property}' is not a public property of {clr.Name}.");

        // Bound once: the static-abstract handler and the active-record load/save on the constructed Entity<T> base.
        Func<string, CancellationToken, Task<object?>> load = async (id, ct) => await Entity<T>.Get(id, ct);
        Func<object, CancellationToken, Task> save = (o, ct) => Entity<T>.Upsert((T)o, ct);
        Func<object, JobContext, CancellationToken, Task> execute = (o, ctx, ct) => T.Execute((T)o, ctx, ct);
        Func<object, string> getId = o => ((T)o).Id;
        Func<string, object> newSingleton = id => { var t = Activator.CreateInstance<T>(); t.Id = id; return t; };

        return new JobTypeBinding(
            clr.FullName!, clr, load, save, execute, getId, newSingleton, chain,
            persist?.Mode ?? JobPersistenceMode.Auto, persist?.Provider, parallelSafe,
            actions, coalesceProps, gateProp);
    }
}
