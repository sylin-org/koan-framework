namespace Koan.Data.Vector.Abstractions;

public sealed record VectorMatch<TKey>(TKey Id, double Score, object? Metadata = null) where TKey : notnull;