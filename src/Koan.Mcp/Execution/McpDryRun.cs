namespace Koan.Mcp.Execution;

/// <summary>
/// AN11 (docs/assessment/09 §14 — A1) — the reserved <c>dry_run</c> argument. When <c>true</c> on a
/// state-mutating tool, the projector runs the full hook/validation pipeline and returns the prospective
/// delta, committing NOTHING. <c>dry_run</c> is a posture every mutating verb accepts — never per-verb
/// wiring. The flag is honored on the entity mutation paths (DB-rehearsable); a custom <c>[McpTool]</c>
/// verb whose effects the framework cannot inspect returns an honest partial rehearsal instead (A10).
/// </summary>
internal static class McpDryRun
{
    /// <summary>The reserved argument name a caller sets to rehearse a mutation.</summary>
    public const string ArgumentName = "dry_run";

    /// <summary>Discovery text shared by generated Entity mutations and custom mutation verbs.</summary>
    public const string SchemaDescription =
        "Request a non-executing rehearsal. Entity mutations return a prospective state delta; custom mutations report their inspectability boundary.";
}
