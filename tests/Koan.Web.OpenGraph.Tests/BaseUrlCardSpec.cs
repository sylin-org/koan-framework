using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>
/// WEB-0078: a card's absolute URLs (<c>og:url</c>, <c>og:image</c>, the canonical link) are rooted at the
/// application's base URL — <c>Koan:Web:ApplicationUrl</c> when it is configured, path included, never the bare
/// request host. Boots through real <c>AddKoan()</c>; the default card needs no entity, so no data host is shared.
/// </summary>
public sealed class BaseUrlCardSpec : IAsyncLifetime
{
    private IntegrationHost _host = default!;
    private string _shellPath = default!;

    public async ValueTask InitializeAsync()
    {
        _shellPath = Path.Combine(Path.GetTempPath(), $"koan-og-baseurl-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(_shellPath, "<html><head><!--KOAN_OPENGRAPH--></head><body>app</body></html>");

        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Web:ApplicationUrl", "https://www.example.com/tangent")
            .WithSetting("Koan:Web:OpenGraph:ShellPath", _shellPath)
            .WithSetting("Koan:Web:OpenGraph:SiteName", "Tangent")
            .WithSetting("Koan:Web:OpenGraph:DefaultImage", "/img/default.png")
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        try { File.Delete(_shellPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Card_urls_carry_the_configured_base_url_and_its_path()
    {
        var renderer = _host.Services.GetRequiredService<IOpenGraphCardRenderer>();

        // The request arrived at the proxy's internal host, under the path base: the card must not echo either.
        var html = await renderer.RenderShellAsync(Navigation("/about", pathBase: "/tangent", host: "10.0.0.7:5080"));

        html.Should().NotBeNull();
        html!.Should().Contain("<meta property=\"og:url\" content=\"https://www.example.com/tangent/about\" />");
        html.Should().Contain("<link rel=\"canonical\" href=\"https://www.example.com/tangent/about\" />");
        html.Should().Contain("<meta property=\"og:image\" content=\"https://www.example.com/tangent/img/default.png\" />");
        html.Should().NotContain("10.0.0.7");
    }

    private static HttpRequest Navigation(string path, string pathBase, string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString(host);
        ctx.Request.PathBase = pathBase;
        ctx.Request.Path = path;
        ctx.Request.Headers.Accept = "text/html";
        return ctx.Request;
    }
}

/// <summary>
/// WEB-0078: without a configured base URL the card derives from the live request — scheme, host and the request's
/// path base — so an application hosted under a path by its server still writes correct absolute URLs.
/// </summary>
public sealed class DerivedBaseUrlCardSpec : IAsyncLifetime
{
    private IntegrationHost _host = default!;
    private string _shellPath = default!;

    public async ValueTask InitializeAsync()
    {
        _shellPath = Path.Combine(Path.GetTempPath(), $"koan-og-derived-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(_shellPath, "<html><head><!--KOAN_OPENGRAPH--></head><body>app</body></html>");

        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Web:OpenGraph:ShellPath", _shellPath)
            .WithSetting("Koan:Web:OpenGraph:SiteName", "Derived")
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        try { File.Delete(_shellPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Card_urls_follow_the_request_including_its_path_base()
    {
        var renderer = _host.Services.GetRequiredService<IOpenGraphCardRenderer>();

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("example.org");
        ctx.Request.PathBase = "/app";
        ctx.Request.Path = "/about";
        ctx.Request.Headers.Accept = "text/html";

        var html = await renderer.RenderShellAsync(ctx.Request);

        html.Should().NotBeNull();
        html!.Should().Contain("<meta property=\"og:url\" content=\"https://example.org/app/about\" />");
    }
}
