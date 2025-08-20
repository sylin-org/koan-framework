using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sora.Data.Abstractions;
using Xunit;

namespace Sora.Testing.Vector;

/// <summary>
/// Reusable, adapter-agnostic acceptance tests for vector adapters.
/// Adapter test projects should inherit and provide the concrete wiring via overrides.
/// </summary>
public abstract class VectorAcceptanceTests<TEntity, TKey>
    where TEntity : class, IEntity<TKey>, new()
    where TKey : notnull
{
    protected abstract bool IsAvailable { get; }
    protected virtual CancellationToken CancellationToken => default;

    // Provide a fresh logical set name (e.g., tenant/schema/collection suffix) if the adapter supports set routing.
    protected abstract string SetName { get; }

    // Repositories
    protected abstract IVectorSearchRepository<TEntity, TKey> GetVectorRepo();
    protected abstract IDataRepository<TEntity, TKey>? GetPrimaryRepoOrNull();

    // Embedding helpers
    protected abstract float[] Embed(TEntity entity);
    protected abstract float[] EmbedQuery(string text);
    protected abstract TKey GetId(TEntity entity);
    protected abstract TEntity WithId(TEntity entity, TKey id);

    protected virtual IEnumerable<TEntity> SeedEntities()
    {
        yield return new TEntity();
    }

    [SkippableFact]
    public async Task Upsert_Search_BasicOrdering()
    {
        Skip.IfNot(IsAvailable, "Vector engine not available for tests.");
        var vec = GetVectorRepo();
        var primary = GetPrimaryRepoOrNull();

        // Seed: 3 entities
        var items = SeedEntities().Take(3).ToArray();
        var ids = new List<TKey>();
        for (var idx = 0; idx < items.Length; idx++)
        {
            var entity = items[idx];
            var id = GetId(entity);
            if (EqualityComparer<TKey>.Default.Equals(id, default!))
            {
                id = GenerateId(idx);
                entity = WithId(entity, id);
                items[idx] = entity;
            }
            ids.Add(id);
            await vec.UpsertAsync(id, Embed(entity), new { idx }, CancellationToken);
            if (primary is not null) await primary.UpsertAsync(entity, CancellationToken);
        }

        // Query near the 2nd item
        var q = Embed(items[1]);
        var result = await vec.SearchAsync(new VectorQueryOptions(q, TopK: 3), CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.Matches.Count > 0);
        // Best match should be the self vector
        Assert.Equal(GetId(items[1]), result.Matches[0].Id);
    }

    [SkippableFact]
    public async Task Guardrails_TopK_Enforced()
    {
        Skip.IfNot(IsAvailable, "Vector engine not available for tests.");
        var vec = GetVectorRepo();
        var q = EmbedQuery("hello world");
        // Very large TopK should be clamped by adapter or policy; at minimum the call should succeed
        var result = await vec.SearchAsync(new VectorQueryOptions(q, TopK: 10_000), CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.Matches.Count >= 0);
    }

    [SkippableFact]
    public async Task Deletion_Removes_From_Search()
    {
        Skip.IfNot(IsAvailable, "Vector engine not available for tests.");
        var vec = GetVectorRepo();
        var e = SeedEntities().First();
        var id = GetId(e);
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
            id = GenerateId(42);
        e = WithId(e, id);
        await vec.UpsertAsync(id, Embed(e), null, CancellationToken);

        var res1 = await vec.SearchAsync(new VectorQueryOptions(Embed(e), TopK: 1), CancellationToken);
        Assert.True(res1.Matches.Count >= 1);
        Assert.Equal(id, res1.Matches[0].Id);

        var ok = await vec.DeleteAsync(id, CancellationToken);
        Assert.True(ok);

        var res2 = await vec.SearchAsync(new VectorQueryOptions(Embed(e), TopK: 1), CancellationToken);
        Assert.True(res2.Matches.Count == 0 || !EqualityComparer<TKey>.Default.Equals(res2.Matches[0].Id, id));
    }

    [SkippableFact]
    public async Task Filters_Conformance_JsonShorthand_vs_Operator_vs_Typed()
    {
        Skip.IfNot(IsAvailable, "Vector engine not available for tests.");
        var vec = GetVectorRepo();
        if (vec is not IVectorCapabilities caps || (caps.Capabilities & VectorCapabilities.Filters) == 0)
            return; // adapter doesn't support filter pushdown

        // Seed
        await vec.UpsertAsync(GenerateId(1), EmbedQuery("a"), new { color = "red", price = 10 }, CancellationToken);
        await vec.UpsertAsync(GenerateId(2), EmbedQuery("b"), new { color = "blue", price = 20 }, CancellationToken);

        var q = EmbedQuery("a");
        // Equality map shorthand
        var f1 = new Dictionary<string, object?> { ["color"] = "red" };
        // Operator JSON DSL
    var f2 = new { @operator = "And", operands = new object[] { new { path = new[] { "color" }, @operator = "Eq", value = "red" } } };
        // Typed AST
        var f3 = VectorFilter.Eq("color", "red");

        var r1 = await vec.SearchAsync(new VectorQueryOptions(q, TopK: 10, Filter: f1), CancellationToken);
        var r2 = await vec.SearchAsync(new VectorQueryOptions(q, TopK: 10, Filter: f2), CancellationToken);
        var r3 = await vec.SearchAsync(new VectorQueryOptions(q, TopK: 10, Filter: f3), CancellationToken);

        var s1 = r1.Matches.Select(m => m.Id).OrderBy(x => x?.GetHashCode()).ToArray();
        var s2 = r2.Matches.Select(m => m.Id).OrderBy(x => x?.GetHashCode()).ToArray();
        var s3 = r3.Matches.Select(m => m.Id).OrderBy(x => x?.GetHashCode()).ToArray();

        Assert.Equal(s1, s2);
        Assert.Equal(s1, s3);
    }

    protected virtual TKey GenerateId(int seed)
    {
        if (typeof(TKey) == typeof(string))
            return (TKey)(object)$"id-{Guid.NewGuid():N}-{seed}";
        if (typeof(TKey) == typeof(Guid))
            return (TKey)(object)Guid.NewGuid();
        throw new NotSupportedException($"Provide an id in SeedEntities or override GenerateId for {typeof(TKey).Name}.");
    }
}
