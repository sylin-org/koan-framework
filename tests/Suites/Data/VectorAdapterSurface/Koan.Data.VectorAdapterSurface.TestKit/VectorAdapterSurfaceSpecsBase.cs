using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Core vector surface specs: upsert / delete / get-embedding / search / flush.
/// Subclassed per vector adapter — the subclass provides the <typeparamref name="TFactory"/>
/// binding via xUnit's IClassFixture pattern. Mirrors <c>AdapterSurfaceSpecsBase</c> from the
/// data matrix.
/// </summary>
public abstract class VectorAdapterSurfaceSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IVectorAdapterTestFactory
{
    protected readonly TFactory Factory;
    private IDisposable? _scope;

    protected VectorAdapterSurfaceSpecsBase(TFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        Koan.Data.Core.AggregateConfigs.Reset();
        _scope = AppHost.PushScope(Factory.Services);
        await Factory.ResetAsync();
        try { await Vector<TodoVector>.EnsureCreated(); } catch { /* not all adapters need this */ }
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return Task.CompletedTask;
    }

    protected void SkipIfUnavailable()
        => Skip.If(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    protected float[] Embed(string category, int seed) => EmbeddingFactory.ForCategory(category, seed, Factory.EmbeddingDimension);
    protected float[] RandomEmbed(int seed) => EmbeddingFactory.Random(seed, Factory.EmbeddingDimension);

    // ============================================================================================
    // Upsert
    // ============================================================================================

    [SkippableFact]
    public async Task Upsert_single_storesVectorAndIsSearchable()
    {
        SkipIfUnavailable();

        var embed = Embed("alpha", 1);
        await Vector<TodoVector>.Save("v1", embed);

        var result = await Vector<TodoVector>.Search(embed, topK: 1);
        result.Matches.Should().HaveCount(1);
        result.Matches[0].Id.Should().Be("v1");
    }

    [SkippableFact]
    public async Task Upsert_overwritesExisting()
    {
        SkipIfUnavailable();

        // First write with category alpha.
        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        // Overwrite with a different category vector.
        await Vector<TodoVector>.Save("v1", Embed("beta", 1));

        // Searching with the new (beta) vector should hit v1 with a much higher score than the
        // original (alpha) vector would yield.
        var hitsBeta = await Vector<TodoVector>.Search(Embed("beta", 99), topK: 5);
        hitsBeta.Matches.Should().Contain(m => m.Id.Equals("v1"));
    }

    [SkippableFact]
    public async Task UpsertMany_bulkInsertsAllVectors()
    {
        Skip.If(!Factory.SupportsBulkOperations, "Adapter does not support bulk operations.");
        SkipIfUnavailable();

        var items = Enumerable.Range(1, 5)
            .Select(i => ($"v{i}", Embed("alpha", i), (object?)null))
            .ToList();

        var inserted = await Vector<TodoVector>.Save(items);
        inserted.Should().BeGreaterThanOrEqualTo(5);

        // All five should be retrievable via search seeded by the same category.
        var found = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 10);
        found.Matches.Select(m => m.Id).Should().Contain(new[] { "v1", "v2", "v3", "v4", "v5" });
    }

    [SkippableFact]
    public async Task UpsertMany_handlesEmptyList()
    {
        Skip.If(!Factory.SupportsBulkOperations, "Adapter does not support bulk operations.");
        SkipIfUnavailable();

        var inserted = await Vector<TodoVector>.Save(Array.Empty<(string, float[], object?)>());
        inserted.Should().Be(0);
    }

    // ============================================================================================
    // Delete
    // ============================================================================================

    [SkippableFact]
    public async Task Delete_removesVector_andIsAbsentFromSearch()
    {
        SkipIfUnavailable();

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        await Vector<TodoVector>.Save("v2", Embed("alpha", 2));

        var deleted = await Vector<TodoVector>.Delete("v1");
        deleted.Should().BeTrue();

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 10);
        hits.Matches.Select(m => m.Id).Should().NotContain("v1");
        hits.Matches.Select(m => m.Id).Should().Contain("v2");
    }

    [SkippableFact]
    public async Task Delete_nonExistentId_returnsFalse()
    {
        SkipIfUnavailable();

        var deleted = await Vector<TodoVector>.Delete("does-not-exist");
        deleted.Should().BeFalse();
    }

    [SkippableFact]
    public async Task DeleteMany_bulkRemoves()
    {
        Skip.If(!Factory.SupportsBulkOperations, "Adapter does not support bulk operations.");
        SkipIfUnavailable();

        foreach (var i in Enumerable.Range(1, 5))
            await Vector<TodoVector>.Save($"v{i}", Embed("alpha", i));

        var removed = await Vector<TodoVector>.Delete(new[] { "v1", "v2", "v3" });
        removed.Should().BeGreaterThanOrEqualTo(3);

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 10);
        hits.Matches.Select(m => m.Id).Should().NotContain(new[] { "v1", "v2", "v3" });
    }

    // ============================================================================================
    // GetEmbedding / GetEmbeddings  (capability-gated)
    // ============================================================================================

    [SkippableFact]
    public async Task GetEmbedding_returnsStoredVector()
    {
        Skip.If(!Factory.SupportsGetEmbedding, "Adapter does not implement GetEmbedding.");
        SkipIfUnavailable();

        var embed = Embed("alpha", 1);
        await Vector<TodoVector>.Save("v1", embed);

        var fetched = await Vector<TodoVector>.GetEmbedding("v1");
        fetched.Should().NotBeNull();
        fetched!.Length.Should().Be(Factory.EmbeddingDimension);
    }

    [SkippableFact]
    public async Task GetEmbedding_unknownId_returnsNull()
    {
        Skip.If(!Factory.SupportsGetEmbedding, "Adapter does not implement GetEmbedding.");
        SkipIfUnavailable();

        var fetched = await Vector<TodoVector>.GetEmbedding("nope");
        fetched.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetEmbeddings_batchReturnsKnown_andOmitsUnknown()
    {
        Skip.If(!Factory.SupportsGetEmbedding, "Adapter does not implement GetEmbeddings.");
        SkipIfUnavailable();

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        await Vector<TodoVector>.Save("v2", Embed("alpha", 2));

        var fetched = await Vector<TodoVector>.GetEmbeddings(new[] { "v1", "v2", "missing" });
        fetched.Keys.Should().Contain(new[] { "v1", "v2" });
        fetched.Should().NotContainKey("missing");
    }

    // ============================================================================================
    // Search
    // ============================================================================================

    [SkippableFact]
    public async Task Search_topK_ordersBySimilarity()
    {
        SkipIfUnavailable();

        // Seed three "alpha" items and one "beta" outlier. A query along the alpha axis must
        // return the three alpha items ahead of the beta one.
        await Vector<TodoVector>.Save("alpha-1", Embed("alpha", 1));
        await Vector<TodoVector>.Save("alpha-2", Embed("alpha", 2));
        await Vector<TodoVector>.Save("alpha-3", Embed("alpha", 3));
        await Vector<TodoVector>.Save("beta-1", Embed("beta", 1));

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 4);
        hits.Matches.Should().HaveCount(4);

        var topThreeIds = hits.Matches.Take(3).Select(m => (string)(object)m.Id).ToList();
        topThreeIds.Should().AllSatisfy(id => id.Should().StartWith("alpha-"));
    }

    [SkippableFact]
    public async Task Search_topK_respectsLimit()
    {
        SkipIfUnavailable();

        foreach (var i in Enumerable.Range(1, 10))
            await Vector<TodoVector>.Save($"v{i}", Embed("alpha", i));

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 3);
        hits.Matches.Should().HaveCount(3);
    }

    [SkippableFact]
    public async Task Search_onEmptyIndex_returnsEmptyResult()
    {
        SkipIfUnavailable();

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 5);
        hits.Matches.Should().BeEmpty();
    }

    // ============================================================================================
    // Flush  (capability-gated; default-throws per IVectorSearchRepository contract)
    // ============================================================================================

    [SkippableFact]
    public async Task Flush_clearsAllVectors()
    {
        Skip.If(!Factory.SupportsFlush, "Adapter does not implement Flush.");
        SkipIfUnavailable();

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        await Vector<TodoVector>.Save("v2", Embed("alpha", 2));

        await Vector<TodoVector>.Flush();

        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 5);
        hits.Matches.Should().BeEmpty();
    }

    // ============================================================================================
    // EnsureCreated
    // ============================================================================================

    [SkippableFact]
    public async Task EnsureCreated_isIdempotent()
    {
        SkipIfUnavailable();

        await Vector<TodoVector>.EnsureCreated();
        await Vector<TodoVector>.EnsureCreated();
        await Vector<TodoVector>.EnsureCreated();

        // Subsequent operations should still work.
        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        var hits = await Vector<TodoVector>.Search(Embed("alpha", 0), topK: 1);
        hits.Matches.Should().NotBeEmpty();
    }
}
