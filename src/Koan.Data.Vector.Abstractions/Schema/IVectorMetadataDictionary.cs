using System.Collections.Generic;

namespace Koan.Data.Vector.Abstractions.Schema;

/// <summary>
/// Provides a deterministic mapping from a metadata model to schema-consistent dictionary values.
/// </summary>
public interface IVectorMetadataDictionary
{
    IReadOnlyDictionary<string, object?> ToDictionary();
}
