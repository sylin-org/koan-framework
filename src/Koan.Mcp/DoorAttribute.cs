using System;

namespace Koan.Mcp;

/// <summary>
/// SEC-0005 (the Door) — opt an entity into capability DISCLOSURE. For a <c>[Door]</c> entity, a verb the caller may
/// NOT invoke (the gate denies) is projected in the <c>koan://entities</c> catalog as a <c>door</c> — named +
/// how-to-unlock (its <c>needs</c>) — instead of vanishing. Default (no <c>[Door]</c>) is a silent <b>Wall</b>
/// (09 §8: never default a capability to Door). PRIVILEGE tiers stay Walls even WITH <c>[Door]</c>: a verb gated on
/// a role is never disclosed (disclosing it would leak that a privileged capability exists — privilege enumeration).
/// The Door is disclosure-only — the verb is still denied on call, and the signpost derives from the SAME gate that
/// enforces it (Description = Enforcement), so it cannot drift.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class DoorAttribute : Attribute
{
}
