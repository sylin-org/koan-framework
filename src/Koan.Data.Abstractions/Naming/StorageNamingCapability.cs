namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// The naming constraints an adapter <em>announces</em> so the framework (<see cref="StorageNameGenerator"/>)
/// can generate physical storage identifiers (table / collection / index names) consistently. This is the
/// single source of truth for an adapter's naming: the adapter declares the rules, the framework applies the
/// algorithm — eliminating the per-adapter ResolveStorage drift (duplicated caches, GUID formatting, and
/// divergent sanitizers).
/// </summary>
public sealed record StorageNamingCapability
{
    /// <summary>Base-name style when no <c>[Storage]/[StorageName]/[StorageNaming]</c> override applies.</summary>
    public StorageNamingStyle Style { get; init; } = StorageNamingStyle.FullNamespace;

    /// <summary>Replacement for the namespace dot in <see cref="StorageNamingStyle.FullNamespace"/> (and the
    /// class/hash join in <see cref="StorageNamingStyle.HashedNamespace"/>).</summary>
    public string Separator { get; init; } = ".";

    /// <summary>Casing applied to the base name.</summary>
    public NameCasing Casing { get; init; } = NameCasing.AsIs;

    /// <summary>Character placed between the base name and the partition token (e.g., '#', '_', '-').</summary>
    public char PartitionSeparator { get; init; } = '#';

    /// <summary>How a partition value is turned into an identifier-safe token.</summary>
    public PartitionTokenPolicy Partition { get; init; } = PartitionTokenPolicy.Default;

    /// <summary>Maximum identifier length in UTF-8 bytes; <c>null</c> = unbounded. When the composed name
    /// exceeds this, the framework keeps a readable prefix and appends a deterministic hash so distinct
    /// (entity, partition) pairs stay distinct. PostgreSQL = 63, SQL Server = 128, Couchbase scope = 30.</summary>
    public int? MaxIdentifierBytes { get; init; }

    /// <summary>When <c>false</c>, the partition is NOT encoded in the name — the adapter isolates it through
    /// a native primitive instead (e.g., Couchbase routes the partition onto a scope).</summary>
    public bool EncodePartitionInName { get; init; } = true;

    /// <summary>Optional runtime base-name override (e.g., Mongo/Couchbase <c>CollectionName</c> callback).
    /// Wins over <see cref="Style"/>; explicit <c>[Storage(Name=...)]</c> on the entity still wins over this.</summary>
    public Func<Type, string?>? NameOverride { get; init; }
}
