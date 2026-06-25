using System;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// ARCH-0102 §3 (Pillar I) — where a managed field's value comes from, which decides the <b>store-aware push</b>:
/// the framework pushes a field's read predicate to a store only if that store can keep the field <i>current</i>.
///
/// <para><b>Derived, never author-typed</b> (ADR Addendum II): set once from the declared shape — a <c>.Field</c>
/// with a live ambient provider is <see cref="AmbientStamped"/>; an <c>.OnDelete</c> / operation override is
/// <see cref="OperationSourced"/>. A <c>[Flags]</c> type so a field can be <b>both</b> (a future moderation axis that
/// ambient-stamps <c>__vis</c> AND has an operation that flips it) — the case a single XOR enum could not express.</para>
///
/// <list type="bullet">
/// <item><see cref="AmbientStamped"/> — written from the ambient on EVERY write of the entity (tenant, moderation).
/// Materialised in every store the entity lives in, so its predicate is enforceable everywhere it is pushed.</item>
/// <item><see cref="OperationSourced"/> — set by a specific operation, not an ambient write (soft-delete's
/// <c>__deleted</c>, set on <c>Delete</c>). Materialised only in the store that ran the operation; pushing its
/// predicate to a secondary store (e.g. an independent vector index) is unenforceable — that is the soft-delete-on-vector
/// class the store-aware push closes.</item>
/// </list>
/// </summary>
[Flags]
public enum FieldProvenance
{
    /// <summary>No declared provenance — never a real managed field; present only for <c>default</c> completeness.</summary>
    None = 0,

    /// <summary>Written from the ambient on every write — present in every store the entity lives in.</summary>
    AmbientStamped = 1,

    /// <summary>Set by an operation (e.g. a delete override) — present only in the store that ran the operation.</summary>
    OperationSourced = 2,
}
