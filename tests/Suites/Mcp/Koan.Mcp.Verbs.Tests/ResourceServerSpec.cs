using AwesomeAssertions;
using Koan.Security.Trust.Issuer;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A host that authenticates every request as a resource server, with the MCP edge requiring its own bearer.</summary>
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
/// ARCH-0141: a bearer the MCP edge accepted for its own resource is the caller, though the application's default scheme would refuse that audience: the credential stops at the edge,
/// and the request made for the tool carries the principal.
/// </summary>
public sealed class ResourceServerSpec(ResourceServerFixture host) : IClassFixture<ResourceServerFixture>
{
    [Fact]
    public async Task A_valid_token_for_the_mcp_resource_reaches_a_route_tool_as_its_subject()
    {
        var tool = await host.Tool("whoami_get", new JObject(), host.Token($"{host.BaseUrl}/mcp"));

        tool.Failed.Should().BeFalse(tool.ToString());
        JObject.Parse(tool.Text)["id"]!.Value<string>().Should().Be("bob");
    }

    [Fact]
    public async Task The_same_subject_answers_through_a_token_for_the_application_on_the_route()
    {
        var route = await host.Route(HttpMethod.Get, "/fidelity/whoami", null, host.Token(host.BaseUrl));

        route.Status.Should().Be(200, route.ToString());
        JObject.Parse(route.Text)["id"]!.Value<string>().Should().Be("bob");
    }

    [Theory]
    [InlineData(new string[0], 403)]
    [InlineData(new[] { "keeper" }, 200)]
    public async Task A_role_in_a_token_is_a_role_on_either_door(string[] roles, int status)
    {
        var route = await host.Route(HttpMethod.Post, "/shelf/own", new JObject(), host.Token(host.BaseUrl, roles));
        var tool = await host.Tool("shelf_own_post", new JObject(), host.Token($"{host.BaseUrl}/mcp", roles));

        route.Status.Should().Be(status, route.ToString());
        tool.Status.Should().Be(status, tool.ToString());
    }

    [Theory]
    [InlineData("/shelf/own", "shelf_own_post", 403)]
    [InlineData("/fidelity/signed-in", "signed_in_post", 200)]
    public async Task ASPNET_answers_an_edges_principal_as_it_answers_a_request(string path, string tool, int status)
    {
        var route = await host.Route(HttpMethod.Post, path, new JObject(), host.Token(host.BaseUrl));
        var called = await host.Tool(tool, new JObject(), host.Token($"{host.BaseUrl}/mcp"));

        route.Status.Should().Be(status, route.ToString());
        called.Status.Should().Be(status, "signed in, a denial is 403 and not 401: {0}", called);
    }
}
