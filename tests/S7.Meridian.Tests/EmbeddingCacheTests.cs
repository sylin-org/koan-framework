using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace S7.Meridian.Tests;

public class EmbeddingCacheTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task FlushAsync_RemovesPersistedEmbeddings()
    {
        var cache = new EmbeddingCache(NullLogger<EmbeddingCache>.Instance);

        // Ensure a clean slate
        await cache.FlushAsync(Ct);

        var content = "Meridian cache test";
        var hash = EmbeddingCache.ComputeContentHash(content);
        var embedding = new[] { 0.1f, 0.2f, 0.3f };
        const string modelId = "granite3.3:8b";
        const string entityType = nameof(Passage);

        await cache.SetAsync(hash, modelId, embedding, entityType, Ct);

        var cached = await cache.GetAsync(hash, modelId, entityType, Ct);
        cached.Should().NotBeNull();
        cached!.Embedding.Should().BeEquivalentTo(embedding);

        var stats = await cache.GetStatsAsync(Ct);
        stats.TotalEntries.Should().BeGreaterThan(0);

        var deleted = await cache.FlushAsync(Ct);
        deleted.Should().BeGreaterThan(0);

        var after = await cache.GetAsync(hash, modelId, entityType, Ct);
        after.Should().BeNull();
    }
}
