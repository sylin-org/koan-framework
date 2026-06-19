using System;
using Koan.Data.Core.Model;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — the mutation audit trail. When an entity is marked <see cref="AuditAttribute"/>, every MUTATING call
/// (write/remove) writes one of these through the normal entity path — so the trail is queryable/streamable like
/// anything else (<c>AgentAction.Query(a =&gt; a.Subject == "...")</c>). Reads are never audited (volume).
/// </summary>
public sealed class AgentAction : Entity<AgentAction>
{
    /// <summary>The acting subject id (the principal's <c>sub</c>/<c>NameIdentifier</c>), or <c>"anonymous"</c>.</summary>
    public string Subject { get; set; } = "";

    /// <summary>The entity name acted on.</summary>
    public string Resource { get; set; } = "";

    /// <summary>The gate action — <c>"write"</c> or <c>"remove"</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>The affected row id, or <c>""</c> for a bulk operation (UpsertMany / Delete-many / by-query / all).</summary>
    public string EntityId { get; set; } = "";

    /// <summary>When the mutation was recorded (UTC).</summary>
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// SEC-0005 — opt an entity into <see cref="AgentAction"/> mutation auditing. Every successful write/remove on the
/// entity writes one audit row; reads are never audited. Inherited so a base type can audit a whole hierarchy.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AuditAttribute : Attribute
{
}
