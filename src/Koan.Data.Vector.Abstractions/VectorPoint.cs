using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>One complete provider-neutral vector point.</summary>
public sealed record VectorPoint<TKey>(TKey Id, ReadOnlyMemory<float> Embedding, DataObject? Metadata)
    where TKey : notnull;
