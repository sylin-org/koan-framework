using System.Net;
using System.Text;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A host that refuses cross-site writes.</summary>
public sealed class CrossSiteWritesFixture : ForumFixture
{
    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Web:RefuseCrossSiteWrites"] = "true",
    };
}

/// <summary>SEC-0039: one check for unsafe requests from another site's page, at every endpoint, <c>/mcp</c> included.</summary>
public sealed class CrossSiteWritesSpec(CrossSiteWritesFixture host) : IClassFixture<CrossSiteWritesFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<HttpStatusCode> Send(string baseUrl, HttpMethod method, string path, string? site, string? origin)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(baseUrl) };
        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get)
        {
            // A cross-site page's simple request: text/plain needs no preflight, and the MCP edge never read the content type.
            request.Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" }.ToString(), Encoding.UTF8, "text/plain");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
        }
        if (site is not null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin.Replace("{self}", baseUrl));
        using var answer = await client.SendAsync(request, Ct);
        return answer.StatusCode;
    }

    [Theory]
    [InlineData("cross-site", null, HttpStatusCode.Forbidden)]
    [InlineData("same-site", null, HttpStatusCode.Forbidden)]
    [InlineData("same-origin", null, HttpStatusCode.OK)]
    [InlineData("none", null, HttpStatusCode.OK)]
    [InlineData("same-origin", "http://evil.example", HttpStatusCode.OK)] // Sec-Fetch-Site decides, and a page cannot forge it
    [InlineData(null, "http://evil.example", HttpStatusCode.Forbidden)]
    [InlineData(null, "null", HttpStatusCode.Forbidden)]
    [InlineData(null, "{self}", HttpStatusCode.OK)]
    [InlineData(null, null, HttpStatusCode.OK)] // not a browser, so no ambient credential
    public async Task A_post_to_mcp_is_refused_from_another_sites_page(string? site, string? origin, HttpStatusCode status)
        => (await Send(host.BaseUrl, HttpMethod.Post, "/mcp", site, origin)).Should().Be(status);

    [Fact]
    public async Task A_post_to_a_route_is_refused_alike_and_a_read_is_not()
    {
        (await Send(host.BaseUrl, HttpMethod.Post, "/request-spec/nothing", "cross-site", null)).Should().Be(HttpStatusCode.Forbidden);
        (await Send(host.BaseUrl, HttpMethod.Get, "/request-spec/read", "cross-site", "http://evil.example")).Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>SEC-0039: nothing is refused until an application turns the check on.</summary>
public sealed class CrossSiteWritesOffSpec(ForumFixture host) : IClassFixture<ForumFixture>
{
    [Fact]
    public async Task Off_a_cross_site_post_reaches_mcp()
        => (await CrossSiteWritesSpec.Send(host.BaseUrl, HttpMethod.Post, "/mcp", "cross-site", "http://evil.example")).Should().Be(HttpStatusCode.OK);
}
