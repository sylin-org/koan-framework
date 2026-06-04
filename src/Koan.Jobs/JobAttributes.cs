namespace Koan.Jobs;

/// <summary>
/// Per-action policy for an <see cref="IKoanJob{TSelf}"/> work-item (JOBS-0005 §5). The only place
/// per-action policy lives. Type-level defaults apply where an action is unspecified. Integer knobs
/// default to <c>-1</c> ("unset → inherit the option default"); time knobs are strings (attributes
/// cannot carry <see cref="System.TimeSpan"/>) parsed as <c>TimeSpan</c> or, for <see cref="Schedule"/>,
/// an interval / cron / sentinel (<c>@boot</c>, <c>@continuous</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class JobActionAttribute : Attribute
{
    public JobActionAttribute(string action) => Action = action;

    /// <summary>The action/stage this policy applies to.</summary>
    public string Action { get; }

    /// <summary>Per-attempt timeout, e.g. <c>"00:10:00"</c>. Cooperative-cancel + ledger-abandon (no hard-kill).</summary>
    public string? Timeout { get; set; }

    /// <summary>Max failure-and-retry attempts before <see cref="JobStatus.Dead"/> (poison). <c>-1</c> = inherit.</summary>
    public int MaxAttempts { get; set; } = -1;

    /// <summary>Chain behavior when this step fails after retries. Default <see cref="OnFailure.Abort"/>.</summary>
    public OnFailure OnFailure { get; set; } = OnFailure.Abort;

    /// <summary>Concurrency lane; defaults to the action name (each action is its own pool).</summary>
    public string? Lane { get; set; }

    /// <summary>Max concurrent executions in this lane. <c>-1</c> = inherit.</summary>
    public int MaxConcurrency { get; set; } = -1;

    /// <summary>Level-trigger: interval (<c>"00:10:00"</c>), cron (<c>"0 2 * * *"</c>), or <c>@boot</c>/<c>@continuous</c>.</summary>
    public string? Schedule { get; set; }

    /// <summary>Total wall-clock from first submit before a perpetually-deferred job dead-letters. Primary runaway guard.</summary>
    public string? Deadline { get; set; }

    /// <summary>Reschedule-count cap (secondary spin-guard). <c>-1</c> = inherit (default high/off).</summary>
    public int MaxReschedules { get; set; } = -1;
}

/// <summary>Declares a linear pipeline: on a step's success the orchestrator persists the work-item and
/// auto-advances to the next stage unless the handler called <c>ctx.StopChain()</c>/<c>ctx.ContinueWith()</c> (JOBS-0005 §5).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class JobChainAttribute : Attribute
{
    public JobChainAttribute(params string[] stages) => Stages = stages ?? Array.Empty<string>();
    public string[] Stages { get; }
}

/// <summary>Declares the idempotency/coalesce key — <c>(type, keys…, action)</c>. Re-delivery is deduped,
/// concurrent duplicates coalesce. Keys are work-item property names (JOBS-0005 §5).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class JobIdempotentAttribute : Attribute
{
    public JobIdempotentAttribute(params string[] keys) => Keys = keys ?? Array.Empty<string>();
    public string[] Keys { get; }
}

/// <summary>Declares the resource the work-item contends for, so the orchestrator can check the shared
/// gate at dispatch <em>without</em> running the handler. The value is a work-item property name whose
/// value forms the gate key (e.g. <c>[JobGate(nameof(Source))]</c>); a dynamic <c>GateKey</c> override
/// can be set at submit time (JOBS-0005 §6.5, Open Q #11).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class JobGateAttribute : Attribute
{
    public JobGateAttribute(string property) => Property = property;
    public string Property { get; }
}
