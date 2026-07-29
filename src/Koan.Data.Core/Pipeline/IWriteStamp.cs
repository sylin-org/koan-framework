namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A synchronous write-stamp: mutate an entity in place just before persist (DATA-0105 Write-stamp stage). Built
/// once per entity type from declared metadata — the built-in identity convention and <c>[Timestamp]</c>, plus any
/// externally-registered contributor (e.g. the classification field-transform's write half, ARCH-0098 §0). Public
/// so a cross-cutting module can author one and register it via <see cref="StorageWriteContributorRegistry"/>
/// (Reference = Intent). Synchronous and allocation-free per the hot-path discipline.
/// </summary>
public interface IWriteStamp
{
    /// <summary>Apply the stamp to <paramref name="entity"/> in place.</summary>
    void Apply(object entity);

    /// <summary>
    /// The stable, explicit apply-order priority (lower runs earlier) — the DATA-0105 §3 "total, stable,
    /// explicit-priority order frozen at discovery" the positional list lacked. Built-ins: identity = 0,
    /// <c>[Timestamp]</c> = 100. A contributor that depends on the id being present orders after 0; a value
    /// transform that protects the final value orders after the stamps. Ties preserve insertion order (the plan
    /// sorts stably).
    /// </summary>
    int Priority { get; }
}
