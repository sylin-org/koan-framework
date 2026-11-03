using System.Net;
using AwesomeAssertions;
using Koan.Security.Trust.Issuer;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A host that authenticates every request as a resource server, with the MCP door closed to nobody-at-all.</summary>
public sealed class ResourceServerFixture : ForumFixture
{
    protected override bool TestScheme => false;

    protected override Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Mcp:RequireAuthentication"] = "true",
        ["Koan:Web:Auth:ResourceServer:Enabled"] = "true",
    };

    public string Token(string audience, params string[] roles)
        => "Bearer " + Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = "bob", Roles = roles }, TimeSpan.FromMinutes(5), audience);
}

/// <summary>
/// SEC-0039: a tool call carries the caller's own credential, so the application's authentication finds the caller on the request made for a route tool
/// as on the route itself. There is one credential: a token for the application on both doors, and no audience of the edge's own.
/// </summary>
public sealed class ResourceServerSpec(ResourceServerFixture host) : IClassFixture<ResourceServerFixture>
{
    [Fact]
    public async Task A_token_for_the_application_is_its_subject_on_both_doors()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token(host.BaseUrl));
        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, host.Token(host.BaseUrl));

        tool.Failed.Should().BeFalse(tool.ToString());
        JObject.Parse(tool.Text)["id"]!.Value<string>().Should().Be("bob");
        tool.Text.Should().Be(route.Text);
    }

    [Fact]
    public async Task A_token_for_an_mcp_audience_is_no_credential_here()
    {
        var call = () => host.Tool("whoami_get", new JObject(), host.Token($"{host.BaseUrl}/mcp"));

        (await call.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Unauthorized");
    }

    [Theory]
    [InlineData(new string[0], 403)]
    [InlineData(new[] { "keeper" }, 200)]
    public async Task A_role_in_a_token_is_a_role_on_either_door(string[] roles, int status)
    {
        var route = await host.Route(HttpMethod.Post, "/shelf/own", new JObject(), host.Token(host.BaseUrl, roles));
        var tool = await host.Tool("shelf_own_post", new JObject(), host.Token(host.BaseUrl, roles));

        route.Status.Should().Be(status, route.ToString());
        tool.Status.Should().Be(status, tool.ToString());
    }

    [Theory]
    [InlineData("/shelf/own", "shelf_own_post", 403)]
    [InlineData("/fidelity/signed-in", "signed_in_post", 200)]
    public async Task ASPNET_answers_a_tool_call_as_it_answers_a_request(string path, string tool, int status)
    {
        var route = await host.Route(HttpMethod.Post, path, new JObject(), host.Token(host.BaseUrl));
        var called = await host.Tool(tool, new JObject(), host.Token(host.BaseUrl));

        route.Status.Should().Be(status, route.ToString());
        called.Status.Should().Be(status, "signed in, a denial is 403 and not 401: {0}", called);
    }
}

/// <summary>SEC-0039: the session cookie is a credential like any other, and a tool call carries it to the route.</summary>
public sealed class CookieToolSpec(CookieDefaultFixture host) : IClassFixture<CookieDefaultFixture>
{
    [Fact]
    public async Task A_cookie_reaches_a_route_tool_as_its_person_and_none_is_a_visitor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var signIn = await client.GetAsync("/request-spec/signin?subject=mara", ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Sent["Cookie"] = signIn.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Koan.cookie", StringComparison.Ordinal)).Split(';')[0];

        var signedIn = await host.Tool("whoami_get", new JObject(), null);
        host.Sent.Remove("Cookie");
        var visitor = await host.Tool("whoami_get", new JObject(), null);

        JObject.Parse(signedIn.Text)["id"]!.Value<string>().Should().Be("mara", signedIn.ToString());
        JObject.Parse(visitor.Text)["id"]?.Value<string>().Should().BeNull(visitor.ToString());
    }
}
