using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Relationships;

/// <summary>Negotiates and executes one child relationship edge for one or many parents.</summary>
public interface IRelationshipQueryExecutor
{
    Task<RelationshipQueryResult<TChild, TKey>> LoadChildren<TParent, TChild, TKey>(
        IReadOnlyCollection<TKey> parentIds,
        string referenceProperty,
        Filter? additionalFilter = null,
        RelationshipQueryPolicy? policy = null,
        string? correlationId = null,
        CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
        where TChild : class, IEntity<TKey>
        where TKey : notnull;
}
