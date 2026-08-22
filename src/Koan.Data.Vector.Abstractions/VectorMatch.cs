using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>One normalized provider-neutral vector match.</summary>
public sealed record VectorMatch<TKey>(TKey Id, double Similarity, DataObject? Metadata = null)
    where TKey : notnull;
