using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>One normalized provider-neutral vector match.</summary>
public sealed record VectorMatch<TKey>(TKey Id, double Similarity, DataObject? Metadata = null)
    where TKey : notnull
{
    /// <summary>Compatibility alias for callers compiled against the pre-annex result name.</summary>
    [Obsolete("Use Similarity. Vector similarity is normalized to [0,1] and higher is closer.")]
    public double Score => Similarity;
}
