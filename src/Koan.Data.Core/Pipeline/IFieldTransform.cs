namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A round-trip field-value transform applied at the data-core chokepoint (ARCH-0098 §0) — the classification
/// encrypt/decrypt is the first consumer. Distinct from <see cref="IWriteStamp"/> (one-way, in-place, identity /
/// <c>[Timestamp]</c>): a field transform is <b>round-trip</b> (write protects, read restores) and must NOT corrupt
/// the caller's instance, so the facade applies <see cref="ApplyOnWrite"/> to a <b>clone</b> that is persisted while
/// the caller keeps the original, and applies <see cref="ApplyOnRead"/> <b>in place</b> on entities returned from a
/// read. Public so a cross-cutting module (<c>Koan.Classification</c>) can author one and register it via
/// <see cref="StorageFieldTransformRegistry"/> (Reference = Intent). The data core never names the axis.
/// </summary>
public interface IFieldTransform
{
    /// <summary>
    /// Protect the transformed fields on <paramref name="entity"/> (e.g. encrypt classified properties). Called by
    /// the facade on a <b>clone</b> of the caller's entity — the persisted payload — so the caller's instance keeps
    /// its plaintext. Mutates in place (the clone).
    /// </summary>
    void ApplyOnWrite(object entity);

    /// <summary>
    /// Restore the transformed fields on <paramref name="entity"/> (e.g. decrypt). Called by the facade in place on
    /// every entity returned from a read, so the caller sees plaintext. Must tolerate a value that was never
    /// transformed (legacy plaintext written before the transform existed) — leave it unchanged.
    /// </summary>
    void ApplyOnRead(object entity);
}
