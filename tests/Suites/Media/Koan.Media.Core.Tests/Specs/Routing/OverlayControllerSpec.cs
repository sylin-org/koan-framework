using System.Net;
using Koan.Media.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Routing;

/// <summary>
/// End-to-end controller tests for the overlay surface — TestServer +
/// real MediaController + default IOverlayResolver backed by
/// InMemoryMediaSource.
/// </summary>
public sealed class OverlayControllerSpec
{
    [Fact]
    public async Task Ad_hoc_url_with_overlay_composites_the_layer()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("host", MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255)));
        await server.Source.AddAsync("logo", MakeSolidPng(40, 40, new Rgba32(255, 0, 0, 255)));

        // overlay=logo (bare form), overlay.position=br, format=png
        var resp = await server.Client.GetAsync(
            "/media/host/png?overlay=logo&overlay.position=br");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.Load<Rgba32>(bytes);
        img[195, 195].R.Should().BeGreaterThan(200, "overlay red present in bottom-right");
    }

    [Fact]
    public async Task Indexed_multi_layer_overlays_route_correctly()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("host", MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255)));
        await server.Source.AddAsync("red", MakeSolidPng(30, 30, new Rgba32(255, 0, 0, 255)));
        await server.Source.AddAsync("green", MakeSolidPng(30, 30, new Rgba32(0, 255, 0, 255)));

        var resp = await server.Client.GetAsync(
            "/media/host/png" +
            "?overlay.0.id=red&overlay.0.position=tl" +
            "&overlay.1.id=green&overlay.1.position=br");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.Load<Rgba32>(bytes);
        img[5, 5].R.Should().BeGreaterThan(200, "red overlay anchored top-left");
        img[195, 195].G.Should().BeGreaterThan(200, "green overlay anchored bottom-right");
    }

    [Fact]
    public async Task Overlay_with_unknown_id_does_not_fail_the_request()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("host", MakeSolidPng(100, 100, new Rgba32(50, 50, 50, 255)));

        var resp = await server.Client.GetAsync("/media/host/png?overlay=missing");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "unknown overlay sources are skipped quietly rather than failing the host render");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.Load<Rgba32>(bytes);
        // Host pixels unchanged
        img[50, 50].R.Should().Be(50);
    }

    [Fact]
    public async Task Recipe_seed_without_overlay_mutator_rejects_overlay_params()
    {
        await using var server = await MediaTestServer.StartAsync(configureServices: services =>
        {
            services.PostConfigure<RecipesOptions>(opts =>
            {
                opts.Recipes["pinned"] = new ConfiguredRecipe
                {
                    Description = "no overlay mutator",
                    Steps = new List<ConfiguredStep>
                    {
                        new() { Op = "encodeAs", Format = "png" },
                    },
                    Mutators = new List<string> { "common" }, // no overlay
                };
            });
        });
        await server.Source.AddAsync("host", MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255)));
        await server.Source.AddAsync("logo", MakeSolidPng(40, 40, new Rgba32(255, 0, 0, 255)));

        // Strict mode would 400; relaxed (default) ignores the params silently.
        await using var strict = await MediaTestServer.StartAsync(
            settings: new() { [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:StrictUnknownParams"] = "true" },
            configureServices: services =>
            {
                services.PostConfigure<RecipesOptions>(opts =>
                {
                    opts.Recipes["pinned"] = new ConfiguredRecipe
                    {
                        Steps = new List<ConfiguredStep> { new() { Op = "encodeAs", Format = "png" } },
                        Mutators = new List<string> { "common" },
                    };
                });
            });
        await strict.Source.AddAsync("host", MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255)));
        await strict.Source.AddAsync("logo", MakeSolidPng(40, 40, new Rgba32(255, 0, 0, 255)));

        var resp = await strict.Client.GetAsync("/media/host/pinned?overlay=logo");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "strict mode + recipe without MutatorKind.Overlay should reject overlay params");
    }

    [Fact]
    public async Task Recipe_with_overlay_mutator_accepts_overlay_overrides()
    {
        await using var server = await MediaTestServer.StartAsync(configureServices: services =>
        {
            services.PostConfigure<RecipesOptions>(opts =>
            {
                opts.Recipes["open-overlay"] = new ConfiguredRecipe
                {
                    Description = "accepts overlay overrides",
                    Steps = new List<ConfiguredStep>
                    {
                        new() { Op = "encodeAs", Format = "png" },
                    },
                    Mutators = new List<string> { "common", "overlay" },
                };
            });
        });
        await server.Source.AddAsync("host", MakeSolidPng(150, 150, new Rgba32(0, 0, 0, 255)));
        await server.Source.AddAsync("logo", MakeSolidPng(30, 30, new Rgba32(0, 0, 255, 255)));

        var resp = await server.Client.GetAsync(
            "/media/host/open-overlay?overlay=logo&overlay.position=tl");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.Load<Rgba32>(bytes);
        img[5, 5].B.Should().BeGreaterThan(200, "blue overlay anchored top-left");
    }

    [Fact]
    public async Task Overlay_diagnostic_recipe_hash_reflects_layer_change()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("host", MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255)));
        await server.Source.AddAsync("logo", MakeSolidPng(20, 20, new Rgba32(255, 0, 0, 255)));

        var noOverlay = await server.Client.GetAsync("/media/host/png");
        var withOverlay = await server.Client.GetAsync("/media/host/png?overlay=logo");
        noOverlay.Headers.GetValues("X-Koan-Media-RecipeHash").Single()
            .Should().NotBe(withOverlay.Headers.GetValues("X-Koan-Media-RecipeHash").Single(),
                "adding an overlay must rotate the recipe fingerprint so caches don't collide");
    }

    private static MemoryStream MakeSolidPng(int width, int height, Rgba32 color)
    {
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = color;
            }
        });
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }
}
