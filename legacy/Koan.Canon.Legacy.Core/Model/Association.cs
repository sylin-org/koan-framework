using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Canon.Model;

public sealed class KeyIndex : Entity<KeyIndex>
{
    public string AggregationKey { get => Id; set => Id = value; }
    [Index]
    public string ReferenceId { get; set; } = default!;
    // New: map aggregation key to CanonicalId (business key)
    [Index]
    public string? CanonicalId { get; set; }
}

public sealed class ReferenceItem : Entity<ReferenceItem>
{
    // UUID is stored in Id (from Entity<>)
    // Canonical business key; unique across the model
    [Index]
    public string CanonicalId { get; set; } = default!;
    // Back-compat alias previously exposed as ReferenceId has been removed; use CanonicalId instead
    [Index]
    public ulong Version { get; set; }
    public bool RequiresProjection { get; set; }
}

