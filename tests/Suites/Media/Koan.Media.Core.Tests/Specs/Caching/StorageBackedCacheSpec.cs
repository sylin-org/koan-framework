using System.Net;
using Koan.Media.Core.Tests.Support;
using Koan.Media.Web.Infrastructure;

namespace Koan.Media.Core.Tests.Specs.Caching;

/// <summary>
/// MEDIA-0007 §c: a previously persisted derivation is served from storage
/// without re-running the recipe pipeline. The new check-then-pipeline-then-
/// write-through flow is the cache-as-storage replacement for the legacy
/// <c>IMediaOutputCache</c> probe.
/// </summary>
public sealed class StorageBackedCacheSpec
{
    [Fact]
    public async Task FirstRequest_runs_pipeline_and_writes_derivation_through_to_storage()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var response = await server.Client.GetAsync("/media/photo/png?w=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Headers.GetValues("X-Koan-Media-FromCache").Single()
            .Should().Be("miss", "cold render is not served from a stored derivation");
        server.Source.DerivationWriteCount.Should().Be(1,
            "the cold render is persisted through IMediaSource.TryStoreDerivationAsync");
        server.Source.DerivationCount.Should().Be(1);
    }

    [Fact]
    public async Task SecondRequest_serves_stored_derivation_without_running_pipeline()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var first = await server.Client.GetAsync("/media/photo/png?w=100");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        server.Source.DerivationWriteCount.Should().Be(1);

        var second = await server.Client.GetAsync("/media/photo/png?w=100");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues("X-Koan-Media-FromCache").Single()
            .Should().Be("hit", "the second request is served from the stored derivation");
        server.Source.DerivationHitCount.Should().Be(1,
            "OpenDerivationAsync returned the persisted bytes");
        server.Source.DerivationWriteCount.Should().Be(1,
            "a hit must never re-render or re-write");

        // Pipeline-only diagnostic headers are absent on the cache-hit path:
        // OutputFormat/FrameCount are populated only when the pipeline runs.
        second.Headers.Contains(HttpHeaderNames.XKoanMediaOutputFormat)
            .Should().BeFalse("storage hit skips pipeline; output diagnostics are not re-emitted");

        var secondBytes = await second.Content.ReadAsByteArrayAsync();
        secondBytes.Should().Equal(firstBytes,
            "the stored derivation is the exact bytes the pipeline produced");
    }

    [Fact]
    public async Task ETag_is_stable_across_cold_render_and_stored_hit()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var first = await server.Client.GetAsync("/media/photo/png?w=120");
        var coldEtag = first.Headers.ETag?.Tag;
        coldEtag.Should().NotBeNullOrEmpty();

        var second = await server.Client.GetAsync("/media/photo/png?w=120");
        var hitEtag = second.Headers.ETag?.Tag;

        hitEtag.Should().Be(coldEtag,
            "ETag is a function of (sourceHash, recipeFingerprint) — independent of cache state");
    }

    [Fact]
    public async Task ContentType_round_trips_through_the_storage_layer()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 600, height: 400));

        var first = await server.Client.GetAsync("/media/photo/webp?w=200");
        first.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");

        var second = await server.Client.GetAsync("/media/photo/webp?w=200");
        second.Content.Headers.ContentType!.MediaType
            .Should().Be("image/webp",
                "the encoder MIME survives the round-trip through TryStoreDerivationAsync/OpenDerivationAsync");
    }

    [Fact]
    public async Task BestEffort_write_through_swallows_storage_failures_without_faulting_response()
    {
        await using var server = await StorageBackedMediaTestServer.StartAsync();
        await server.Source.AddSourceAsync("photo", Fixtures.WideJpeg(width: 400, height: 300));
        server.Source.WriteFault = (_, _) => new InvalidOperationException("storage boom");

        var response = await server.Client.GetAsync("/media/photo/png?w=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "MEDIA-0007 §c: the write is best-effort; the pipeline still returns the bytes");
        response.Headers.GetValues("X-Koan-Media-FromCache").Single().Should().Be("miss");
        server.Source.DerivationCount.Should().Be(0,
            "the faulting write did not produce a stored row");
    }
}
