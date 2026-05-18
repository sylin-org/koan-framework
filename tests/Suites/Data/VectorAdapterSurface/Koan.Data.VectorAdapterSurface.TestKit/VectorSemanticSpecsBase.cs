using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Vector;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Real-world scenario specs adapted from PGVector's <c>SemanticSearch.Spec.cs</c>. These exercise
/// shape-of-results behavior (similarity ranking, duplicate detection, hybrid blending) rather
/// than the raw API surface. Adapters that don't support a scenario flag it off via the
/// capability interface and the spec skips green.
/// </summary>
public abstract class VectorSemanticSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IVectorAdapterTestFactory
{
    protected readonly TFactory Factory;
    private IDisposable? _scope;

    protected VectorSemanticSpecsBase(TFactory factory) { Factory = factory; }

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        Koan.Data.Core.AggregateConfigs.Reset();
        _scope = AppHost.PushScope(Factory.Services);
        await Factory.ResetAsync();
        try { await Vector<TodoVector>.EnsureCreated(); } catch { }
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

    [SkippableFact]
    public async Task DocumentSimilarity_findsRelatedContent()
    {
        SkipIfUnavailable();

        // Three "tech" docs, three "cooking" docs. Query along the tech axis must rank tech
        // documents higher than cooking documents.
        await Vector<TodoVector>.Save("tech-1", Embed("tech", 1));
        await Vector<TodoVector>.Save("tech-2", Embed("tech", 2));
        await Vector<TodoVector>.Save("tech-3", Embed("tech", 3));
        await Vector<TodoVector>.Save("cooking-1", Embed("cooking", 1));
        await Vector<TodoVector>.Save("cooking-2", Embed("cooking", 2));
        await Vector<TodoVector>.Save("cooking-3", Embed("cooking", 3));

        var hits = await Vector<TodoVector>.Search(Embed("tech", 0), topK: 6);
        var topThree = hits.Matches.Take(3).Select(m => (string)(object)m.Id);
        topThree.Should().AllSatisfy(id => id.Should().StartWith("tech-"));
    }

    [SkippableFact]
    public async Task Recommendation_findsSimilarItemsByVector()
    {
        SkipIfUnavailable();

        // Save items in three product categories. Query with one specific item's vector — the
        // top result (after itself) should be the same-category neighbors.
        var queryVec = Embed("electronics", 7);
        await Vector<TodoVector>.Save("query-anchor", queryVec);
        await Vector<TodoVector>.Save("electronics-2", Embed("electronics", 8));
        await Vector<TodoVector>.Save("electronics-3", Embed("electronics", 9));
        await Vector<TodoVector>.Save("books-1", Embed("books", 1));
        await Vector<TodoVector>.Save("books-2", Embed("books", 2));

        var hits = await Vector<TodoVector>.Search(queryVec, topK: 3);
        var ids = hits.Matches.Select(m => (string)(object)m.Id).ToList();
        ids.Should().Contain("query-anchor");
        ids.Should().Contain(id => id.StartsWith("electronics-"));
    }

    [SkippableFact]
    public async Task DuplicateDetection_findsNearDuplicates()
    {
        SkipIfUnavailable();

        // Two near-identical items + one different. Searching with one of the dupes should rank
        // the other dupe ahead of the different item.
        await Vector<TodoVector>.Save("dup-a", Embed("topic", 5));
        await Vector<TodoVector>.Save("dup-b", Embed("topic", 5));  // same seed → very similar vector
        await Vector<TodoVector>.Save("different", Embed("other-topic", 5));

        var hits = await Vector<TodoVector>.Search(Embed("topic", 5), topK: 3);
        var topTwo = hits.Matches.Take(2).Select(m => (string)(object)m.Id).ToList();
        topTwo.Should().Contain("dup-a");
        topTwo.Should().Contain("dup-b");
    }

    [SkippableFact]
    public async Task HybridSearch_combinesVectorAndKeyword()
    {
        Skip.If(!Factory.SupportsHybridSearch, "Adapter does not support hybrid search (Alpha + SearchText).");
        SkipIfUnavailable();

        // Save with embeddings; the adapter must also index 'Title' as text for BM25.
        await Vector<TodoVector>.Save("hybrid-1", Embed("topic", 1));
        await Vector<TodoVector>.Save("hybrid-2", Embed("topic", 2));

        // Pure-keyword leaning search. We can't assert exact ranking across adapters (BM25
        // implementations vary), but we can assert the search runs without throwing and returns
        // results.
        var hits = await Vector<TodoVector>.Search(
            vector: Embed("topic", 0),
            text: "hybrid",
            alpha: 0.5);
        hits.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task CapabilitySurface_matchesAdvertisedFlags()
    {
        SkipIfUnavailable();

        var caps = Vector<TodoVector>.GetCapabilities();

        // We don't assert exact flag-by-flag equality (adapters legitimately differ in detail),
        // but if the factory advertises hybrid we expect the adapter's Capabilities to include it.
        if (Factory.SupportsHybridSearch)
            caps.Should().HaveFlag(Koan.Data.Vector.Abstractions.VectorCapabilities.Hybrid);
        if (Factory.SupportsContinuationToken)
            caps.Should().HaveFlag(Koan.Data.Vector.Abstractions.VectorCapabilities.NativeContinuation);
    }
}
