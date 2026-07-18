using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 — the canonical audit channel for identity/access mutations: <c>actor / action / target /
/// before → after / context / occurred_at</c>.
/// <para>
/// Identity lifecycle hooks emit best-effort records. Optional hash chaining detects later edits, reordering, or
/// removal; storage-level append-only enforcement and SIEM delivery are not provided.
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
