using System.Net;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Caching;

/// <summary>
/// MEDIA-0007 §c (last paragraph): conditional-GET handling sits above the
/// storage probe, so <c>If-None-Match</c> short-circuits before
/// <see cref="Web.Routing.IMediaSource.OpenDerivationAsync"/> is even
/// consulted. The unified flow must preserve ETag semantics across the
/// cold-render / stored-hit boundary.
/// </summary>
public sealed class ConditionalGETSpec
{
    [Fact]
    public async Task ColdRender_returns_200_and_an_ETag()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        var response = await server.Client.GetAsync("/media/photo/png?w=120");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IfNoneMatch_with_matching_ETag_returns_304_after_cold_render()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        var cold = await server.Client.GetAsync("/media/photo/png?w=120");
        var etag = cold.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty();

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/media/photo/png?w=120");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag!);
        var response = await server.Client.SendAsync(conditional);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified,
            "If-None-Match matches the cold-render ETag → 304");
    }

    [Fact]
    public async Task IfNoneMatch_with_matching_ETag_returns_304_after_storage_hit()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        // Warm the storage: first cold render persists a derivation.
        var cold = await server.Client.GetAsync("/media/photo/png?w=120");
        cold.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = cold.Headers.ETag?.Tag;
        server.Source.DerivationCount.Should().Be(1, "warm-up persisted the derivation");

        // Now issue a conditional GET against the warmed source. The 304 must
        // come back without touching either the pipeline or the storage probe.
        var hitsBefore = server.Source.DerivationHitCount;

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/media/photo/png?w=120");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag!);
        var response = await server.Client.SendAsync(conditional);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        server.Source.DerivationHitCount.Should().Be(hitsBefore,
            "the storage probe is bypassed when If-None-Match matches");
    }

    [Fact]
    public async Task IfNoneMatch_with_stale_ETag_falls_through_to_storage_hit()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        // Warm the storage.
        await server.Client.GetAsync("/media/photo/png?w=120");
        server.Source.DerivationCount.Should().Be(1);

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/media/photo/png?w=120");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", "\"stale-etag-no-match\"");
        var response = await server.Client.SendAsync(conditional);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "mismatched If-None-Match means deliver the body");
        response.Headers.GetValues("X-Koan-Media-FromCache").Single()
            .Should().Be("hit", "the warm derivation is still served from storage");
        server.Source.DerivationHitCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NewETag_on_cache_miss_matches_the_ETag_returned_on_subsequent_hit()
    {
        // ETag is a function of (sourceHash, recipeFingerprint), so the
        // value the cold render returns must be exactly what a stored-hit
        // response returns afterwards.
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));

        var cold = await server.Client.GetAsync("/media/photo/png?w=120");
        var coldEtag = cold.Headers.ETag?.Tag;

        var warm = await server.Client.GetAsync("/media/photo/png?w=120");
        var warmEtag = warm.Headers.ETag?.Tag;

        warmEtag.Should().Be(coldEtag,
            "ETag survives the round-trip through storage so conditional clients still validate");
    }
}
