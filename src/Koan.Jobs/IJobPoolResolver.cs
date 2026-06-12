namespace Koan.Jobs;

/// <summary>
/// Provides the live member set for a named resource pool, consulted at claim time (JOBS-0007).
/// Implement this interface and register via <c>AddJobPoolResolver&lt;T&gt;()</c>; the orchestrator calls
/// <see cref="GetMembersAsync"/> before each claim attempt so the pool reflects runtime state (members added,
/// paused, or removed since the job was submitted). Implementations must be thread-safe and cheap on the
/// hot path; do not perform expensive fanout here — cache the member list and refresh it on a background timer.
/// </summary>
public interface IJobPoolResolver
{
    /// <summary>The pool name this resolver handles. Must match the <c>[JobPool]</c> attribute value on the job type.</summary>
    string PoolName { get; }

    /// <summary>Max simultaneous in-flight jobs per member. Default 1 (strict serial-per-server).</summary>
    int CapacityPerMember => 1;

    /// <summary>Return the currently available member keys. The orchestrator picks the first member with open
    /// capacity, stamps it as the job's GateKey, and claims the record atomically. Members that are paused or
    /// removed should be absent from this list so no new work is dispatched to them.</summary>
    Task<IReadOnlyList<string>> GetMembersAsync(CancellationToken ct);
}
