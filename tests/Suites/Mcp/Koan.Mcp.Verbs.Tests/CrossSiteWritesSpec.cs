using System.Net;
using System.Text;
using AwesomeAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A host that turns the cross-site check off.</summary>
public sealed class CrossSiteWritesOffFixture : ForumFixture
{
    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Web:RefuseCrossSiteWrites"] = "false",
        ["Koan:Mcp:AllowedOrigins:0"] = "http://evil.example",
    };
}

/// <summary>SEC-0039: one check, on with nothing set, for an unsafe request carrying a cookie from another site's page, at every endpoint, <c>/mcp</c> included.</summary>
public sealed class CrossSiteWritesSpec(ForumFixture host) : IClassFixture<ForumFixture>
{
    private const string Cookie = ".AspNetCore.Koan.cookie=whatever";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<HttpStatusCode> Send(string baseUrl, HttpMethod method, string path, string? site, string? origin,
        string? cookie = Cookie, string contentType = "application/json")
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(baseUrl) };
        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get)
        {
            request.Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list" }.ToString(), Encoding.UTF8, contentType);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
        }
        if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
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
    [InlineData("same-origin", "http://evil.example", HttpStatusCode.Forbidden)] // Sec-Fetch-Site decides the cross-site check, and the edge holds Origin to the application's own besides (AI-0042)
    [InlineData(null, "http://evil.example", HttpStatusCode.Forbidden)]
    [InlineData(null, "null", HttpStatusCode.Forbidden)]
    [InlineData(null, "{self}", HttpStatusCode.OK)]
    [InlineData(null, null, HttpStatusCode.OK)] // not a browser's
    public async Task A_post_to_mcp_carrying_a_cookie_is_refused_from_another_sites_page(string? site, string? origin, HttpStatusCode status)
        => (await Send(host.BaseUrl, HttpMethod.Post, "/mcp", site, origin)).Should().Be(status);

    /// <summary>A forgery borrows an ambient credential, so a request with none has nothing to borrow: a client posting to an OAuth token endpoint is one.</summary>
    [Fact]
    public async Task A_cross_site_post_with_no_cookie_is_not_judged()
        => (await Send(host.BaseUrl, HttpMethod.Post, "/mcp", "cross-site", null, cookie: null)).Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task A_post_to_a_route_is_refused_alike_and_a_read_is_not()
    {
        (await Send(host.BaseUrl, HttpMethod.Post, "/request-spec/nothing", "cross-site", null)).Should().Be(HttpStatusCode.Forbidden);
        (await Send(host.BaseUrl, HttpMethod.Get, "/request-spec/read", "cross-site", "http://evil.example")).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A JSON-RPC message is JSON: a form or text/plain, which a page of another site sends with no preflight, never reaches a tool.</summary>
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data")]
    public async Task Mcp_takes_json_alone(string contentType)
        => (await Send(host.BaseUrl, HttpMethod.Post, "/mcp", null, null, cookie: null, contentType: contentType)).Should().Be(HttpStatusCode.UnsupportedMediaType);
}

/// <summary>SEC-0039: an application with a front end on another site turns the check off, and answers for that front end, whose origin
/// it names to the edge (AI-0042).</summary>
public sealed class CrossSiteWritesOffSpec(CrossSiteWritesOffFixture host) : IClassFixture<CrossSiteWritesOffFixture>
{
    [Fact]
    public async Task Off_a_cross_site_post_with_a_cookie_reaches_mcp()
        => (await CrossSiteWritesSpec.Send(host.BaseUrl, HttpMethod.Post, "/mcp", "cross-site", "http://evil.example")).Should().Be(HttpStatusCode.OK);
}
