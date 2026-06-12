using Koan.Core;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// Non-generic marker for an entity that defines background jobs. Marked <see cref="KoanDiscoverableAttribute"/>
/// so concrete implementors are auto-collected into <c>KoanRegistry</c> at build/boot, and the orchestrator
/// binds each type's static handler once at startup (no per-dispatch reflection). JOBS-0005 §4.1.
/// </summary>
[KoanDiscoverable]
public interface IKoanJob
{
}

/// <summary>
/// The entity authoring surface: <c>MyModel : Entity&lt;MyModel&gt;, IKoanJob&lt;MyModel&gt;</c>. One static
/// handler runs every action — single-action jobs ignore <c>ctx.Action</c>; multi-action jobs switch on it.
/// The work-item passed to <see cref="Execute"/> is the <b>mutable saga state</b>; orchestration metadata is
/// the read-only <see cref="JobContext.State"/> ("work-item != Job", JOBS-0005 §3).
/// </summary>
public interface IKoanJob<TSelf> : IKoanJob
    where TSelf : Entity<TSelf>, IKoanJob<TSelf>
{
    /// <summary>Execute one action on the work-item. Signal outcome by returning (success/advance),
    /// throwing (failure → retry), or calling a <see cref="JobContext"/> control verb (reschedule/backoff/branch).</summary>
    static abstract Task Execute(TSelf job, JobContext ctx, CancellationToken ct);
}
