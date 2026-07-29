using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

/// <summary>Framework-compiled isolation values and predicate passed to a vector adapter.</summary>
public sealed record VectorScope(
    string Identity,
    DataObject Values,
    Filter? Predicate,
    Filter? ResidualPredicate = null)
{
    public static VectorScope Unscoped { get; } = new(
        string.Empty,
        new DataObject(Array.Empty<DataProperty>()),
        null,
        null);

    public bool IsEmpty => Identity.Length == 0 && Predicate is null;
}
