using Koan.Canon.Domain.Metadata;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Canon value objects participate in canonization but are scoped to a canonical parent.
/// </summary>
/// <typeparam name="TValue">Concrete value-object type.</typeparam>
public abstract class CanonValueObject<TValue> : Entity<TValue>
    where TValue : CanonValueObject<TValue>, new()
{
    private CanonMetadata _metadata = new();

    /// <summary>
    /// Reference to the owning canonical entity.
    /// </summary>
    [Index]
    public string? CanonicalReferenceId { get; set; }

    /// <summary>
    /// Value-object specific metadata snapshot.
    /// </summary>
    public CanonMetadata Metadata
    {
        get => _metadata;
        set => _metadata = value ?? new CanonMetadata();
    }
}
