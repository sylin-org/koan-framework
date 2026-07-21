using Koan.Data.Abstractions;

namespace Koan.Data.Core.Relationships;

/// <summary>Children grouped by the parent key together with the negotiated execution decision.</summary>
public sealed record RelationshipQueryResult<TEntity, TKey>(
    IReadOnlyDictionary<TKey, IReadOnlyList<TEntity>> ByParent,
    RelationshipExecutionDecision Decision)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull;
