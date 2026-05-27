using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Media.Core.Tests.Support;
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

        var response = await server.Client.GetAsync("/media/huge");
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

        var response = await server.Client.GetAsync("/media/manyframes");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("X-Koan-Media-LimitExceeded").Single().Should().Be("maxFrameCount");
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
}

