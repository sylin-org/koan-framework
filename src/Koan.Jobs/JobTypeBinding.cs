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
    private readonly Func<object, IServiceProvider, CancellationToken, Task<string?>>? _gateResolver;

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
        PropertyInfo? gateProp,
        Func<object, IServiceProvider, CancellationToken, Task<string?>>? gateResolver,
        string? poolName)
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
        _gateResolver = gateResolver;
        PoolName = poolName;
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

    /// <summary>Pool name when the type carries <c>[JobPool]</c> (JOBS-0007). Null for non-pool jobs.
    /// The gate key is NOT resolved at submit — it is stamped at claim time by the ledger using a
    /// live <see cref="IJobPoolResolver"/>. Mutually exclusive with <c>[JobGate]</c>.</summary>
    public string? PoolName { get; }

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

    /// <summary>The distinct lanes this type's work can land in — every declared action, every chain stage, and the
    /// default single-action lane. Used to enumerate the lane universe for the JOBS-0008 lane-fair claim (lanes derive
    /// from <c>[JobAction]</c>, so the registry is the authoritative lane set — no discovery scan).</summary>
    public IEnumerable<string> Lanes(JobsOptions o)
    {
        var actions = new HashSet<string>(_actions.Keys, StringComparer.Ordinal);
        foreach (var stage in Chain) actions.Add(stage);
        actions.Add("");   // the default single-action lane (action token "")
        return actions.Select(a => ResolvePolicy(a, o).Lane).Distinct(StringComparer.Ordinal);
    }

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

    /// <summary>The gate key from a <c>[JobGate(property)]</c> (sync, no DI), or null. For the method-form resolver
    /// use <see cref="ResolveGateKey"/>; check <see cref="HasGateResolver"/> first.</summary>
    public string? GateKey(object workItem) => _gateProp?.GetValue(workItem)?.ToString();

    /// <summary>True when <c>[JobGate]</c> names an async resolver method rather than a property (JOBS-0005 §18).</summary>
    public bool HasGateResolver => _gateResolver is not null;

    /// <summary>Resolve the gate key at submit: the async resolver if the type declared one (<c>[JobGate(nameof(Method))]</c>
    /// where the member is <c>Task&lt;string?&gt; Method(IServiceProvider, CancellationToken)</c>), else the property value.</summary>
    public Task<string?> ResolveGateKey(object workItem, IServiceProvider services, CancellationToken ct)
        => _gateResolver is not null ? _gateResolver(workItem, services, ct) : Task.FromResult(GateKey(workItem));

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
        var pool = clr.GetCustomAttribute<JobPoolAttribute>(inherit: true);
        if (gate is not null && pool is not null)
            throw new InvalidOperationException(
                $"{clr.Name} cannot have both [JobGate] and [JobPool]: use [JobPool] for runtime pools and [JobGate] for static resource keys.");
        var persist = clr.GetCustomAttribute<JobPersistenceAttribute>(inherit: true);
        var parallelSafe = clr.GetCustomAttribute<ParallelSafeAttribute>(inherit: true) is not null;

        var coalesceProps = (idem?.Keys ?? Array.Empty<string>())
            .Select(k => clr.GetProperty(k, PropFlags)
                ?? throw new InvalidOperationException($"[JobIdempotent] key '{k}' is not a public property of {clr.Name}."))
            .ToArray();
        // [JobGate] names a property (sync value) OR a method (async, DI-capable resolver — JOBS-0005 §18).
        PropertyInfo? gateProp = null;
        Func<object, IServiceProvider, CancellationToken, Task<string?>>? gateResolver = null;
        if (gate is not null)
        {
            gateProp = clr.GetProperty(gate.Property, PropFlags);
            if (gateProp is null)
            {
                var method = clr.GetMethod(gate.Property, PropFlags, binder: null,
                    types: new[] { typeof(IServiceProvider), typeof(CancellationToken) }, modifiers: null);
                if (method is null || method.ReturnType != typeof(Task<string?>))
                    throw new InvalidOperationException(
                        $"[JobGate] member '{gate.Property}' on {clr.Name} must be a public property, or a public method " +
                        $"'Task<string?> {gate.Property}(IServiceProvider, CancellationToken)'.");
                var del = (Func<T, IServiceProvider, CancellationToken, Task<string?>>)Delegate.CreateDelegate(
                    typeof(Func<T, IServiceProvider, CancellationToken, Task<string?>>), method);
                gateResolver = (o, sp, ct) => del((T)o, sp, ct);
            }
        }

        // Bound once: the static-abstract handler and the active-record load/save on the constructed Entity<T> base.
        Func<string, CancellationToken, Task<object?>> load = async (id, ct) => await Entity<T>.Get(id, ct);
        Func<object, CancellationToken, Task> save = (o, ct) => Entity<T>.Upsert((T)o, ct);
        Func<object, JobContext, CancellationToken, Task> execute = (o, ctx, ct) => T.Execute((T)o, ctx, ct);
        Func<object, string> getId = o => ((T)o).Id;
        Func<string, object> newSingleton = id => { var t = Activator.CreateInstance<T>(); t.Id = id; return t; };

        return new JobTypeBinding(
            clr.FullName!, clr, load, save, execute, getId, newSingleton, chain,
            persist?.Mode ?? JobPersistenceMode.Auto, persist?.Provider, parallelSafe,
            actions, coalesceProps, gateProp, gateResolver, pool?.PoolName);
    }
}
