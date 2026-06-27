using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 — the canonical append-only audit channel for identity/access mutations: <c>actor / action / target /
/// before → after / context / occurred_at</c>.
/// <para>
/// <b>P0 scope is the entity declaration only.</b> Lifecycle-seam auto-emit (audit-by-construction) lands in P1,
/// and append-only enforcement + hash-chaining + SIEM streaming land in P3. The richer field set (vs SEC-0005's
/// <c>AgentAction</c>) is deliberate; whether <c>AuditEvent</c> supersedes <c>AgentAction</c> is an open
/// convergence question tracked in the ADR.
/// </para>
/// </summary>
public sealed class AuditEvent : Entity<AuditEvent>, IAmbientExempt
{
    /// <summary>Who performed the action — the principal subject, or the impersonator (<c>actor</c>) when impersonating (P3).</summary>
    public string? Actor { get; set; }

    /// <summary>The affected person/subject, when distinct from <see cref="Actor"/>.</summary>
    public string? Subject { get; set; }

    /// <summary>The action verb, e.g. <c>identity.reconciled</c> / <c>identity.suspended</c> / <c>role.granted</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>The target entity (name + id), when applicable.</summary>
    public string? Target { get; set; }

    /// <summary>JSON snapshot of the target before the mutation (null for creates).</summary>
    public string? Before { get; set; }

    /// <summary>JSON snapshot of the target after the mutation (null for deletes).</summary>
    public string? After { get; set; }

    /// <summary>Request context (IP / user-agent / correlation), as JSON.</summary>
    public string? Context { get; set; }

    /// <summary>When the action occurred (set once, on creation).</summary>
    [Timestamp]
    public DateTimeOffset OccurredAt { get; set; }

    // --- Tamper-evidence (Layer 3, optional hash-chaining; null/0 when chaining is off) ---

    /// <summary>Monotonic position in the hash chain (0-based); 0 with a null <see cref="Hash"/> = unchained.</summary>
    public long Sequence { get; set; }

    /// <summary>The previous event's <see cref="Hash"/> this one chains from (<c>GENESIS</c> for the first).</summary>
    public string? PrevHash { get; set; }

    /// <summary>SHA-256 over (sequence | prevHash | canonical content). Editing any past event breaks the chain.</summary>
    public string? Hash { get; set; }
}
