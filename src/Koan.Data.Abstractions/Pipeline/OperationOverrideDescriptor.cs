namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// A declarative <b>operation-semantics override</b> (ARCH-0101 §4) — the most powerful composition plane, kept
/// <b>data, not an arbitrary interceptor</b> (descriptor-not-callback, conformance-checkable). An axis declares that
/// an operation <i>means</i> something other than its default — the canonical case is soft-delete: <c>Delete</c> ⇒
/// set <see cref="Field"/> to <see cref="OnDeleteValue"/> instead of physically removing the row. The facade applies
/// it at the delete chokepoint: it loads the visible (read-scoped) row and re-persists it with the field set, through
/// the UNGUARDED operation-override write channel (<see cref="ManagedFieldWriteScope.Overrides"/>) so the mutable
/// state field is injected but never wrongly conflict-guarded.
///
/// <para>All OTHER isolation is retained — the load is still read-scoped (tenant / moderation IDOR), so a soft-delete
/// can only soft-remove a row the caller can see. The escape verb (<c>.HardDelete()</c>) rides the generic
/// <see cref="OperationOverrideBypass"/> — it bypasses ONLY this override (physical remove), never the read-scoping.</para>
/// </summary>
/// <param name="Field">The persisted field the delete sets (e.g. <c>"__deleted"</c>) — a managed field the axis also registers.</param>
/// <param name="OnDeleteValue">The value to set on <c>Delete</c> (e.g. <c>true</c>).</param>
/// <param name="AppliesTo">Which entity types this override governs (e.g. <c>t =&gt; t has [SoftDelete]</c>).</param>
public sealed record OperationOverrideDescriptor(
    string Field,
    object? OnDeleteValue,
    Func<Type, bool> AppliesTo);
