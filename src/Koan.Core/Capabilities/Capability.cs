namespace Koan.Core.Capabilities;

/// <summary>
/// A single, strongly-typed capability token — the atomic unit an infrastructure provider
/// declares, the framework negotiates against, and the boot report renders. The <see cref="Id"/>
/// is a stable dotted string (e.g. <c>"query.linq"</c>, <c>"write.bulkUpsert"</c>) so a token
/// serializes into self-report / <c>/.well-known</c> payloads as-is.
/// <para>
/// Authors never compose the string by hand — they use the per-pillar catalogs
/// (<c>DataCaps.Query.Linq</c>, <c>VectorCaps.Knn</c>, …). The raw-string constructor is the
/// extension escape hatch, not the path.
/// </para>
/// See ARCH-0084.
/// </summary>
public readonly record struct Capability
{
    /// <summary>Creates a token from its stable dotted id.</summary>
    public Capability(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A capability id must be a non-empty string.", nameof(id));
        Id = id;
    }

    /// <summary>The stable dotted identity, e.g. <c>"query.linq"</c>. Equality is by this value.</summary>
    public string Id { get; }

    /// <inheritdoc />
    public override string ToString() => Id;
}
