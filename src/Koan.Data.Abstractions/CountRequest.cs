using System;
using System.Linq.Expressions;

namespace Koan.Data.Abstractions;

public sealed class CountRequest<TEntity>
{
    public CountStrategy Strategy { get; init; } = CountStrategy.Exact;
    public Expression<Func<TEntity, bool>>? Predicate { get; init; }
    public string? RawQuery { get; init; }
    public object? ProviderQuery { get; init; }
    public DataQueryOptions? Options { get; init; }
}
