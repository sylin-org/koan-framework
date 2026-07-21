using System.Collections.Generic;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — a single typed grant the principal must hold: the <c>has:</c> vocabulary. Grants within one
/// <see cref="AccessBag"/> are AND-combined. Each kind maps to exactly one principal check (see
/// <see cref="AccessGateEvaluator"/>). The string parser emits <see cref="Scope"/>/<see cref="Role"/>/<see cref="Claim"/>;
/// <see cref="RoleAnyOf"/> exists only for lowering multiple <c>[Authorize(Roles=)]</c> attributes (AND across the
/// groups, OR within each) without bloating the bag with a list-of-lists.
/// </summary>
public abstract record Grant
{
    /// <summary>An OAuth scope the principal must hold — <c>has:scope:x</c>.</summary>
    public sealed record Scope(string Value) : Grant;

    /// <summary>A role held as a typed grant — <c>has:role:y</c>. Same check as <c>is:</c>, distinct authoring intent.</summary>
    public sealed record Role(string Value) : Grant;

    /// <summary>A claim of <paramref name="Type"/> whose value equals <paramref name="Value"/> — <c>has:claim:z=v</c>.</summary>
    public sealed record Claim(string Type, string Value) : Grant;

    /// <summary>Any-of a role set (OR within); used in <see cref="AccessBag.HasAllOf"/> it is AND-combined with the
    /// rest of the bag. Emitted only by multi-<c>[Authorize(Roles=)]</c> lowering.</summary>
    public sealed record RoleAnyOf(IReadOnlyList<string> Roles) : Grant;
}
