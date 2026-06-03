using System.Net;
using Koan.Media.Core.Tests.Support;
using Koan.Media.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Media.Core.Tests.Specs.Routing;

/// <summary>
/// Diagnostic headers don't just have to be present — their VALUES must
/// be correct. A header that lies (wrong fingerprint, wrong frame count,
/// wrong format) is worse than one that's missing: it actively misleads
/// the operator debugging a problem in DevTools.
/// </summary>
public sealed class DiagnosticHeaderSpec
{
    [Fact]
    public async Task RecipeHash_header_matches_effective_recipe_fingerprint()
    {
        await using var server = await MediaTestServer.StartAsync(configureServices: services =>
        {
            services.PostConfigure<RecipesOptions>(opts =>
            {
                opts.Recipes["test-fingerprint"] = new ConfiguredRecipe
                {
                    Steps = new List<ConfiguredStep>
                    {
                        new() { Op = "resize", Width = 100, Height = 100 },
                        new() { Op = "encodeAs", Format = "png" },
                    },
                };
            });
        });
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 400, height: 400));

        var registry = server.App.Services.GetRequiredService<IMediaRecipeRegistry>();
        var expected = registry.Find("test-fingerprint")!.Fingerprint();

        var response = await server.Client.GetAsync("/media/photo/test-fingerprint");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaRecipeHash).Single()
            .Should().Be(expected, "header must match the registry's reported fingerprint");
    }

    [Fact]
    public async Task ETag_embeds_source_short_hash_and_recipe_fingerprint()
    {
        await using var server = await MediaTestServer.StartAsync();
        var srcStream = Fixtures.WideJpeg(width: 200, height: 200);
        var sourceHash = await server.Source.AddAsync("photo", srcStream);

        var response = await server.Client.GetAsync("/media/photo/png");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = response.Headers.ETag!.Tag.Trim('"');
        // Expected shape: <sourceShortHash>-<recipeFingerprint>
        etag.Split('-').Should().HaveCount(2);
        var sourceShort = sourceHash.Length >= 12 ? sourceHash[..12] : sourceHash;
        etag.Should().StartWith(sourceShort, "ETag must embed the source's short hash");
    }

    [Fact]
    public async Task FrameCount_header_matches_animation_frame_count()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("anim", Fixtures.AnimatedWebp(frames: 4));

        // Modifier forces the pipeline path; diagnostic headers are a
        // pipeline-output concept (FrameCount is the OUTPUT count), so
        // they're only set when the pipeline runs. The bare URL is now
        // a raw-byte passthrough and doesn't stamp these.
        var response = await server.Client.GetAsync("/media/anim?w=80");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaFrameCount).Single()
            .Should().Be("4", "header must match output frame count for animated sources");
    }

    [Fact]
    public async Task FrameCount_is_1_for_static_sources()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("static", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/static?w=200");
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaFrameCount).Single().Should().Be("1");
    }

    [Fact]
    public async Task FrameCount_drops_to_1_after_ExtractFrame()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("anim", Fixtures.AnimatedWebp(frames: 5));

        var response = await server.Client.GetAsync("/media/anim?frame=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaFrameCount).Single().Should().Be("1");
    }

    [Fact]
    public async Task SourceFormat_and_OutputFormat_diverge_when_recipe_transcodes()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 100, height: 100));

        // Source is JPEG, recipe forces PNG output via the format shortcut.
        var response = await server.Client.GetAsync("/media/photo/png");
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaSourceFormat).Single().Should().Be("jpeg");
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaOutputFormat).Single().Should().Be("png");
    }

    [Fact]
    public async Task SourceFormat_equals_OutputFormat_when_format_preserved()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 100, height: 100));

        // No format shortcut, no ?format= override → preserved. Use ?w=
        // to push through the pipeline so the diagnostic headers (which
        // are a pipeline-output concept) get stamped; bare URL is now a
        // raw-byte passthrough with no header decoration.
        var response = await server.Client.GetAsync("/media/photo?w=50");
        var sourceFmt = response.Headers.GetValues(HttpHeaderNames.XKoanMediaSourceFormat).Single();
        var outputFmt = response.Headers.GetValues(HttpHeaderNames.XKoanMediaOutputFormat).Single();
        sourceFmt.Should().Be(outputFmt);
        outputFmt.Should().Be("jpeg");
    }

    [Fact]
    public async Task Recipe_header_carries_ad_hoc_marker_when_no_seed()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo?w=50");
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaRecipe).Single().Should().Be("ad-hoc");
    }

    [Fact]
    public async Task FromCache_header_is_hit_on_304_response()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var first = await server.Client.GetAsync("/media/photo/png?w=50");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Get, "/media/photo/png?w=50");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await server.Client.SendAsync(request);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        second.Headers.GetValues("X-Koan-Media-FromCache").Single().Should().Be("hit",
            "304 responses should report hit, not miss");
    }
}
