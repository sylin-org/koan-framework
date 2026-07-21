using System;

namespace Koan.Mcp;

/// <summary>
/// AN4 — opt-in MCP tool annotations for custom <c>[McpTool]</c> verbs. Entity tools derive their
/// readOnly/destructive/idempotent hints mechanically from the verb (Query/Get → readOnly, Delete* →
/// destructive, Upsert* → idempotent). A hand-written verb gains NOTHING automatically — the dangerous
/// ones are exactly the ones that must be marked — so a verb author opts in with these markers.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpReadOnlyAttribute : Attribute
{
}

/// <summary>AN4 — marks a custom <c>[McpTool]</c> verb as potentially destructive (irreversible updates).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpDestructiveAttribute : Attribute
{
}

/// <summary>AN4 — marks a custom <c>[McpTool]</c> verb as idempotent (repeating it has no additional effect).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpIdempotentAttribute : Attribute
{
}
