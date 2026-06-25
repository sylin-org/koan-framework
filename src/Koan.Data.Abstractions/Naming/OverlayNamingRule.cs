using System;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// ARCH-0102 §5 — an adapter-declared rule for spelling framework-injected <b>overlay</b> fields (the
/// <see cref="Marker"/>-prefixed managed / isolation-discriminator fields) in a store whose identifier law
/// rejects the default marker. Weaviate is the canonical case: it queries over GraphQL, which RESERVES a
/// leading <c>__</c>, so <c>__koan_tenant</c> is illegal there.
///
/// <para><b>Override-only:</b> a <c>null</c> rule (the default) leaves names untouched — Mongo / SQL Server
/// declare nothing. <b>Declare-don't-call:</b> the adapter declares this once; the <i>framework</i> applies the
/// SAME rule at write-stamp AND read-filter, so the two spellings are bijective by construction (FC-6) — there is
/// no per-operation call site to forget. The rule rewrites a whole namespace (any <see cref="Marker"/>-prefixed
/// field), so a new isolation axis is covered without the adapter knowing it exists.</para>
///
/// <para>The transform must be <b>injective</b> (no two overlay names collapse) and stay in a reserved-by-convention
/// namespace so it cannot collide with a real entity member — for Weaviate, <c>__ → koan_</c> keeps every overlay
/// field in the letter-leading, reserved <c>koan_</c> prefix.</para>
/// </summary>
public sealed record OverlayNamingRule(string MarkerReplacement)
{
    /// <summary>The framework overlay marker — a leading double underscore (the convention for an injected field).</summary>
    public const string Marker = "__";

    /// <summary>
    /// Rewrite a single overlay field name. A name that does not carry the <see cref="Marker"/> is returned
    /// unchanged (user metadata is never in the overlay namespace).
    /// </summary>
    public string Apply(string name)
        => name is not null && name.StartsWith(Marker, StringComparison.Ordinal)
            ? MarkerReplacement + name[Marker.Length..]
            : name!;
}
