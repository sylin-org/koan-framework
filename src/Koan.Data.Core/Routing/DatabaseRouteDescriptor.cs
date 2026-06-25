using System;

namespace Koan.Data.Core.Routing;

/// <summary>
/// A single <see cref="Koan.Data.Core.Axes.AxisMode.Database"/> route (ARCH-0102 §3) the expander produces from a
/// Database-mode <c>[DataAxis]</c>. It pairs the axis's per-entity activation predicate with the per-operation
/// <b>source-key provider</b> (the <c>.Field</c> value provider) — the value that selects which data source the
/// framework routes the operation to. Carried in <see cref="DatabaseRouteRegistry"/> and consulted by
/// <c>AdapterResolver</c> after an explicit <c>EntityContext.Source</c> (which always wins).
/// </summary>
/// <param name="AxisId">The logical axis id — the registry dedup key and the <c>.Explain</c> / fail-closed label.</param>
/// <param name="AppliesTo">The per-entity activation predicate (ambient-independent; memoizable). Entities it returns
/// <c>false</c> for are never auto-routed by this axis.</param>
/// <param name="SourceKeyProvider">Reads the current ambient axis value; its non-blank string form is the source name
/// to route to. A <c>null</c>/blank result means "no route from this axis right now" and the resolver falls through to
/// its normal chain (e.g. the Default source). The registry treats null/blank uniformly as fall-through — it does NOT
/// fail closed on an unset ambient. For a <b>strict isolation</b> axis where an unset ambient is a programming error,
/// the provider itself should throw rather than return null (a declarative <c>NullKeyBehavior.FailClosed</c> opt-in is
/// tracked as a Phase 3 follow-on). When several routes apply to one entity type, the first registered wins.</param>
public sealed record DatabaseRouteDescriptor(
    string AxisId,
    Func<Type, bool> AppliesTo,
    Func<object?> SourceKeyProvider);
