using System.Runtime.CompilerServices;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Core.Selection;

namespace Koan.Data.Core;

/// <summary>Adds pointwise direct-relationship enrichment to finite and asynchronous Entity sources.</summary>
public static class RelationshipExtensions
{
    /// <summary>
    /// Loads one direct relationship graph for every Entity in the supplied finite source, preserving
    /// source order and multiplicity. Child edges use strict bounded-query negotiation.
    /// </summary>
    public static Task<IReadOnlyList<RelationshipGraph<TEntity>>> Relatives<TEntity, TKey>(
        this IEnumerable<Entity<TEntity, TKey>> entities,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => entities.Relatives(RelationshipQueryPolicy.Strict, ct);

    /// <summary>
    /// Loads one direct relationship graph for every Entity in the supplied finite source using the
    /// explicit child-edge execution policy, preserving source order and multiplicity.
    /// </summary>
    public static async Task<IReadOnlyList<RelationshipGraph<TEntity>>> Relatives<TEntity, TKey>(
        this IEnumerable<Entity<TEntity, TKey>> entities,
        RelationshipQueryPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(policy);

        var source = EntityCardinality.Many(AsTyped<TEntity, TKey>(entities), ct);
        var results = new List<RelationshipGraph<TEntity>>();
        await foreach (var graph in ResolveLoader().Load<TEntity, TKey>(source, policy, ct).ConfigureAwait(false))
        {
            results.Add(graph);
        }

        return results;
    }

    /// <summary>
    /// Lazily loads one direct relationship graph for every Entity yielded by the source. The input is
    /// consumed once, output is source-ordered, and child edges use strict bounded-query negotiation.
    /// </summary>
    public static IAsyncEnumerable<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this IAsyncEnumerable<Entity<TEntity, TKey>> entities,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => entities.Relatives(RelationshipQueryPolicy.Strict, ct);

    /// <summary>
    /// Lazily loads one direct relationship graph for every Entity yielded by the source using the
    /// explicit child-edge execution policy. Work is bounded to one internal source batch at a time.
    /// </summary>
    public static IAsyncEnumerable<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this IAsyncEnumerable<Entity<TEntity, TKey>> entities,
        RelationshipQueryPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(policy);

        var source = EntityCardinality.Stream(AsTyped<TEntity, TKey>(entities, ct), ct);
        return ResolveLoader().Load<TEntity, TKey>(source, policy, ct);
    }

    private static IEnumerable<TEntity> AsTyped<TEntity, TKey>(IEnumerable<Entity<TEntity, TKey>> entities)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        foreach (var entity in entities)
        {
            yield return entity is TEntity typed
                ? typed
                : throw InvalidReceiver<TEntity, TKey>(entity);
        }
    }

    private static async IAsyncEnumerable<TEntity> AsTyped<TEntity, TKey>(
        IAsyncEnumerable<Entity<TEntity, TKey>> entities,
        [EnumeratorCancellation] CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        await foreach (var entity in entities.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return entity is TEntity typed
                ? typed
                : throw InvalidReceiver<TEntity, TKey>(entity);
        }
    }

    private static InvalidOperationException InvalidReceiver<TEntity, TKey>(Entity<TEntity, TKey>? entity)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new(
            entity is null
                ? $"The Entity relationship source yielded null for {typeof(TEntity).Name}."
                : $"The Entity relationship source yielded {entity.GetType().Name}, but its declared model is {typeof(TEntity).Name}.");

    private static RelationshipGraphLoader ResolveLoader()
        => AppHost.GetRequiredService<RelationshipGraphLoader>("relationship graph loading");
}
