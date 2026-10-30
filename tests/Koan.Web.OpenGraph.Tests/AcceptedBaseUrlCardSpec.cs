using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>
/// WEB-0081: a card's absolute URLs are rooted at the URL the application accepted, read at render, so accepting
/// another moves <c>og:url</c>, <c>og:image</c> and the canonical link on the next render, whatever the request's host.
/// </summary>
public sealed class AcceptedBaseUrlCardSpec : IDisposable
{
    private readonly string _shellPath = Path.Combine(Path.GetTempPath(), $"koan-og-accepted-{Guid.NewGuid():N}.html");

    public AcceptedBaseUrlCardSpec()
        => File.WriteAllText(_shellPath, "<html><head><!--KOAN_OPENGRAPH--></head><body>app</body></html>");

    public void Dispose()
    {
        try { File.Delete(_shellPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Card_urls_follow_the_accepted_url_without_a_restart()
    {
        var baseUrl = new KoanWebBaseUrl(null, store: new MemoryApplicationUrlStore());
        var options = new OpenGraphOptions { Enabled = true, ShellPath = _shellPath, SiteName = "Accepted", DefaultImage = "/img/default.png" };
        var renderer = new OpenGraphCardRenderer(new TestOptionsMonitor<OpenGraphOptions>(options), new ShellCache(), new SocialCardRegistry(), baseUrl);

        await baseUrl.Accept("https://first.example", TestContext.Current.CancellationToken);
        var first = await renderer.RenderShellAsync(Navigation());

        await baseUrl.Accept("https://second.example", TestContext.Current.CancellationToken);
        var second = await renderer.RenderShellAsync(Navigation());

        first!.Should().Contain("<meta property=\"og:url\" content=\"https://first.example/about\" />");
        second!.Should().Contain("<meta property=\"og:url\" content=\"https://second.example/about\" />");
        second.Should().Contain("<link rel=\"canonical\" href=\"https://second.example/about\" />");
        second.Should().Contain("<meta property=\"og:image\" content=\"https://second.example/img/default.png\" />");
        second.Should().NotContain("10.0.0.7");
    }

    private static HttpRequest Navigation()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("10.0.0.7:5080");
        ctx.Request.Path = "/about";
        ctx.Request.Headers.Accept = "text/html";
        return ctx.Request;
    }

    private sealed class MemoryApplicationUrlStore : IApplicationUrlStore
    {
        private string? _value;

        public Task<string?> Load(CancellationToken ct) => Task.FromResult(_value);

        public Task Save(string applicationUrl, CancellationToken ct)
        {
            _value = applicationUrl;
            return Task.CompletedTask;
        }
    }
}
