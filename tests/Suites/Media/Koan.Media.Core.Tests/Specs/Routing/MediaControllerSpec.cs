using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Tests.Support;
using Koan.Media.Web.Caching;
using Koan.Media.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Routing;

/// <summary>
/// End-to-end controller tests via TestServer. Covers URL grammar,
/// override layering, format-preservation guarantees, conditional
/// GETs, diagnostic headers, and recipe introspection.
/// </summary>
public sealed class MediaControllerSpec
{
    [Fact]
    public async Task GetOriginal_returns_source_bytes_format_preserved()
    {
        await using var server = await MediaTestServer.StartAsync();
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        await server.Source.AddAsync("anim", src);

        var response = await server.Client.GetAsync("/media/anim");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var roundTrip = Image.Load(bytes);
        roundTrip.Frames.Count.Should().Be(3, "controller preserves animation on default GET");
    }

    [Fact]
    public async Task GetWithFormatShortcut_reencodes_as_requested_format()
    {
        await using var server = await MediaTestServer.StartAsync();
        await using var src = Fixtures.WideJpeg(width: 400, height: 300);
        await server.Source.AddAsync("photo", src);

        var response = await server.Client.GetAsync("/media/photo/png");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    /// <summary>
    /// Bare <c>/media/{id}</c> with no recipe, no query modifiers, and no
    /// AllowedOutputFormats negotiation surface MUST stream the source
    /// bytes verbatim with their stored ContentType. The image pipeline
    /// is bypassed entirely - the endpoint becomes content-addressable
    /// raw byte serving when no transform is requested.
    ///
    /// <para>Regression for the gposingway video-as-source case: an
    /// MP4 stored under an ArticleMedia hash was 422'ing on bare
    /// <c>/media/{id}</c> because the controller always pushed bytes
    /// through the image decoder, which rightly rejected MP4 magic.
    /// The article's <c>&lt;video&gt;</c> tag then loaded nothing.</para>
    /// </summary>
    [Fact]
    public async Task GetOriginal_streams_raw_bytes_with_stored_ContentType_for_non_image_source()
    {
        await using var server = await MediaTestServer.StartAsync();
        // Synthesize "video bytes" - an MP4 magic header is enough to
        // make ImageSharp reject them as not-an-image. The body is
        // arbitrary; we only need round-trip fidelity, not playable
        // video.
        var fakeMp4 = new byte[] {
            0x00, 0x00, 0x00, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0xDE, 0xAD, 0xBE, 0xEF,
        };
        await using var src = new MemoryStream(fakeMp4);
        await server.Source.AddAsync("clip", src, contentType: "video/mp4");

        var response = await server.Client.GetAsync("/media/clip");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "non-image source must round-trip cleanly through the no-recipe path");
        response.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4",
            "the stored ContentType must surface verbatim");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(fakeMp4, "raw bytes must be byte-identical to the source");
    }

    [Fact]
    public async Task GetOriginal_with_query_modifier_runs_pipeline_even_when_unsuitable()
    {
        // ?w=200 is a modifier - it injects a ResizeStep onto the
        // recipe, so the no-transform fast-path does NOT fire. The
        // image pipeline then refuses to decode the non-image source
        // and surfaces a typed 422. This is the contract: modifiers
        // mean "transform", and the framework should not silently
        // hand back raw bytes when a transform was requested.
        await using var server = await MediaTestServer.StartAsync();
        var fakeMp4 = new byte[] {
            0x00, 0x00, 0x00, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
        };
        await using var src = new MemoryStream(fakeMp4);
        await server.Source.AddAsync("clip", src, contentType: "video/mp4");

        var response = await server.Client.GetAsync("/media/clip?w=200");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetWithUnknownSeed_returns_404()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo/nonexistent-recipe");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWithUnknownMediaId_returns_404()
    {
        await using var server = await MediaTestServer.StartAsync();
        var response = await server.Client.GetAsync("/media/missing-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdHoc_resize_applies()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var response = await server.Client.GetAsync("/media/photo?w=400");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var img = Image.Load(bytes);
        img.Width.Should().Be(400);
        img.Height.Should().Be(300, "single-axis resize preserves aspect ratio");
    }

    [Fact]
    public async Task AdHoc_format_shortcut_with_resize_applies_both()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var response = await server.Client.GetAsync("/media/photo/png?w=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var img = Image.Load(bytes);
        img.Width.Should().Be(200);
    }

    [Fact]
    public async Task UrlParam_order_is_irrelevant()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        var a = await (await server.Client.GetAsync("/media/photo?w=200&format=png")).Content.ReadAsByteArrayAsync();
        var b = await (await server.Client.GetAsync("/media/photo?format=png&w=200")).Content.ReadAsByteArrayAsync();

        a.Should().Equal(b, "stage-ordered pipeline yields identical bytes regardless of URL param order");
    }

    [Fact]
    public async Task DiagnosticHeaders_are_present_on_render()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo/png?w=100");
        response.Headers.Contains(HttpHeaderNames.XKoanMediaRecipe).Should().BeTrue();
        response.Headers.Contains(HttpHeaderNames.XKoanMediaRecipeHash).Should().BeTrue();
        response.Headers.GetValues(HttpHeaderNames.XKoanMediaOutputFormat).Should().ContainSingle("png");
    }

    [Fact]
    public async Task ETag_supports_conditional_GET()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var first = await server.Client.GetAsync("/media/photo/png?w=100");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty();

        var request = new HttpRequestMessage(HttpMethod.Get, "/media/photo/png?w=100");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag!);
        var second = await server.Client.SendAsync(request);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task MaxSourceMegapixels_limit_returns_400_with_diagnostic()
    {
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:MaxSourceMegapixels"] = "1",
        });
        // 2000x2000 = 4MP, exceeds 1MP cap
        await server.Source.AddAsync("huge", Fixtures.WideJpeg(width: 2000, height: 2000));

        // Use ?w=500 to push the request through the pipeline; the
        // no-transform fast path doesn't run the decoder so source limits
        // (which guard decoder memory) don't fire there.
        var response = await server.Client.GetAsync("/media/huge?w=500");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Should().Contain(h => h.Key == "X-Koan-Media-LimitExceeded");
        var limitHeader = response.Headers.GetValues("X-Koan-Media-LimitExceeded").Single();
        limitHeader.Should().Be("maxSourceMegapixels");
    }

    [Fact]
    public async Task MaxFrameCount_limit_returns_400_with_diagnostic()
    {
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:MaxFrameCount"] = "2",
        });
        await server.Source.AddAsync("manyframes", Fixtures.AnimatedWebp(frames: 5));

        // Modifier forces the pipeline path so the frame-count guard fires.
        var response = await server.Client.GetAsync("/media/manyframes?w=50");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("X-Koan-Media-LimitExceeded").Single().Should().Be("maxFrameCount");
    }

    /// <summary>
    /// The no-transform fast-path bypasses the decoder, so source-side
    /// limits (which exist to guard decoder memory allocation) do NOT
    /// fire on a bare <c>/media/{id}</c> URL. This is by design: when no
    /// recipe and no modifiers are requested, the framework just streams
    /// the bytes - there's no allocation to guard against. The cost is
    /// bytes-on-the-wire, which is bounded by the source's stored size.
    /// </summary>
    [Fact]
    public async Task Bare_URL_skips_source_limits_because_no_decode_happens()
    {
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:MaxSourceMegapixels"] = "1",
        });
        await server.Source.AddAsync("huge", Fixtures.WideJpeg(width: 2000, height: 2000));

        var response = await server.Client.GetAsync("/media/huge");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "bare URL streams raw bytes without decoder allocation, so source-limit guard doesn't apply");
    }

    [Fact]
    public async Task OutputDimension_limit_returns_400()
    {
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:MaxOutputEdge"] = "1000",
        });
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo?w=5000");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Contains("X-Koan-Media-LimitExceeded").Should().BeTrue();
    }

    [Fact]
    public async Task AdHoc_disabled_in_production_rejects_param_requests()
    {
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:AllowAdHoc"] = "false",
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:StrictUnknownParams"] = "true",
        });
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo?w=400");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdHoc_decode_failure_returns_422()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("broken", Fixtures.NotAnImage(byteCount: 32));

        var response = await server.Client.GetAsync("/media/broken/png");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Recipes_endpoint_returns_format_shortcuts_and_aliases()
    {
        await using var server = await MediaTestServer.StartAsync();
        var response = await server.Client.GetAsync("/media/recipes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        json.GetProperty("formatShortcuts").EnumerateArray().Should().NotBeEmpty();
        json.GetProperty("paramAliases").TryGetProperty("w", out _).Should().BeTrue();
        json.GetProperty("adHocSteps").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SingleRecipe_endpoint_returns_404_for_unknown()
    {
        await using var server = await MediaTestServer.StartAsync();
        var response = await server.Client.GetAsync("/media/recipes/nope");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SingleRecipe_endpoint_returns_full_recipe_with_canonical_shape()
    {
        // Per-test config recipe — no global [MediaRecipe] scan pollution.
        await using var server = await MediaTestServer.StartAsync(configureServices: services =>
        {
            services.PostConfigure<RecipesOptions>(opts =>
            {
                opts.Recipes["per-test-recipe"] = new ConfiguredRecipe
                {
                    Description = "per-test recipe scoped to a single spec",
                    Steps = new List<ConfiguredStep>
                    {
                        new() { Op = "resize", Width = 200 },
                        new() { Op = "encodeAs", Format = "webp", Quality = 80 },
                    },
                    Mutators = new List<string> { "common" },
                };
            });
        });

        var response = await server.Client.GetAsync("/media/recipes/per-test-recipe");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("per-test-recipe");
        json.Should().Contain("\"fingerprint\":");
        json.Should().Contain("\"steps\":");
    }

    [Fact]
    public async Task AppSettings_form_wraps_recipe()
    {
        // Register a config-equivalent recipe via direct DI override so the
        // test doesn't depend on InMemoryCollection nested-step binding.
        await using var server = await MediaTestServer.StartAsync(configureServices: services =>
        {
            services.PostConfigure<RecipesOptions>(opts =>
            {
                opts.Recipes["hero"] = new ConfiguredRecipe
                {
                    Description = "test hero",
                    Steps = new List<ConfiguredStep>
                    {
                        new() { Op = "resize", Width = 800 },
                        new() { Op = "encodeAs", Format = "webp", Quality = 80 },
                    },
                };
            });
        });

        var response = await server.Client.GetAsync("/media/recipes/hero?as=appsettings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("Koan");
        text.Should().Contain("Media");
        text.Should().Contain("Recipes");
        text.Should().Contain("hero");
    }

    [Fact]
    public async Task AdHoc_bg_solid_with_contain_returns_padded_canvas()
    {
        // End-to-end through the controller: URL-level bg= flows through
        // the parser, mutator allowlist, and pipeline into the bytes.
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 1200, height: 600));

        var response = await server.Client.GetAsync(
            "/media/photo?crop=800x600&fit=contain&bg=00ff00&w=600&h=600&format=png");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(bytes);
        img.Width.Should().Be(600, "canvas extended to the requested 1:1 box");
        img.Height.Should().Be(600);
        var corner = img[10, 10];
        corner.G.Should().BeGreaterThan(200, "padding corner is the green bg, not source");
    }

    [Fact]
    public async Task AdHoc_bg_blur_returns_target_canvas()
    {
        // bg=blur with no radius — composer picks a default. Smoke test
        // through the controller URL grammar.
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 1200, height: 600));

        var response = await server.Client.GetAsync(
            "/media/photo?crop=800x600&fit=contain&bg=blur&w=600&h=600&format=png");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var img = SixLabors.ImageSharp.Image.Load(bytes);
        img.Width.Should().Be(600);
        img.Height.Should().Be(600);
    }

    [Fact]
    public async Task AdHoc_bg_without_crop_is_rejected_400()
    {
        // bg without crop has nothing to fill — the parser rejects it as
        // a typo guard rather than silently no-op'ing.
        await using var server = await MediaTestServer.StartAsync(settings: new()
        {
            [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:StrictUnknownParams"] = "true",
        });
        await server.Source.AddAsync("photo", Fixtures.WideJpeg());

        var response = await server.Client.GetAsync("/media/photo?bg=red");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // MEDIA-0007: the legacy IMediaOutputCache cache stays as a transition
    // shim this release (deleted in MEDIA-0008). The interface and the
    // record it returns are marked Obsolete; this test still exercises the
    // legacy path, so the warnings are suppressed locally.
#pragma warning disable CS0618
    [Fact]
    public async Task OutputCache_first_request_renders_and_writes_through_then_serves_from_cache()
    {
        var cache = new CaptureReplayCache();
        await using var server = await MediaTestServer.StartAsync(
            configureServices: services => services.AddSingleton<IMediaOutputCache>(cache));
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 800, height: 600));

        // Cold request: miss → pipeline runs → write-through.
        var first = await server.Client.GetAsync("/media/photo/png?w=100");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.GetValues("X-Koan-Media-FromCache").Single().Should().Be("miss");
        cache.SetCount.Should().Be(1, "the cold render is persisted write-through");
        var bytes1 = await first.Content.ReadAsByteArrayAsync();

        // Warm request: same URL → cache hit → pipeline skipped, no re-write.
        var second = await server.Client.GetAsync("/media/photo/png?w=100");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues("X-Koan-Media-FromCache").Single().Should().Be("hit");
        second.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        cache.SetCount.Should().Be(1, "a cache hit does not re-render or re-write");
        var bytes2 = await second.Content.ReadAsByteArrayAsync();

        bytes2.Should().Equal(bytes1, "the served bytes come from the cached render");
    }

    /// <summary>
    /// In-memory <see cref="IMediaOutputCache"/> that stores whatever the
    /// controller writes and replays it on subsequent reads — enough to prove
    /// the read-through / write-through seam without a filesystem.
    /// </summary>
    private sealed class CaptureReplayCache : IMediaOutputCache
    {
        private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _store = new();
        public int SetCount { get; private set; }

        public Task<MediaCacheHit?> TryGetAsync(string id, string fingerprint, CancellationToken ct = default)
        {
            if (_store.TryGetValue(Key(id, fingerprint), out var entry))
            {
                return Task.FromResult<MediaCacheHit?>(
                    new MediaCacheHit(new MemoryStream(entry.Bytes, writable: false), entry.ContentType));
            }
            return Task.FromResult<MediaCacheHit?>(null);
        }

        public Task SetAsync(string id, string fingerprint, MediaOutput output, CancellationToken ct = default)
        {
            SetCount++;
            _store[Key(id, fingerprint)] = (output.Bytes, output.ContentType);
            return Task.CompletedTask;
        }

        private static string Key(string id, string fingerprint) => $"{id}|{fingerprint}";
    }
#pragma warning restore CS0618
}

