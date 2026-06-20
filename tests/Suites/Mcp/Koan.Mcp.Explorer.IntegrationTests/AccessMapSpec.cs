using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Mcp.Explorer.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>
/// WEB-0072 D5 — the privileged Capability Access Map (god-view): the un-redacted inverse of the per-caller
/// surface. It shows every requirement INCLUDING role walls, so it must never be served unprivileged.
/// </summary>
public sealed class AccessMapSpec : IClassFixture<ExplorerFixture>
{
    private readonly ExplorerFixture _fx;
    private readonly HttpClient _client;

    public AccessMapSpec(ExplorerFixture fx)
    {
        _fx = fx;
        _client = fx.NewClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Access_map_shows_the_full_requirement_including_walls()
    {
        // The test host is Development, so the god-view is served.
        var res = await _client.GetAsync("/mcp/access-map.json", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var map = JObject.Parse(await res.Content.ReadAsStringAsync(Ct));
        var entities = (JArray)map["entities"]!;

        // The WALL the per-caller map hides is VISIBLE here, with its full (role) requirement.
        var adminlog = Entity(entities, "adminlog");
        adminlog.Should().NotBeNull("the god-view shows walls — that is its whole point");
        ((string?)adminlog!["access"]!["read"]).Should().Contain("admin");

        // The scope-gated entity shows the exact scope.
        var docvault = Entity(entities, "docvault")!;
        ((string?)docvault["access"]!["read"]).Should().Contain("docs:read");

        // The public entity reads as anonymous (allow-by-default rendered explicitly).
        var trinket = Entity(entities, "trinket")!;
        ((string?)trinket["access"]!["read"]).Should().Be("anonymous");
    }

    private static JObject? Entity(JArray entities, string name)
        => entities.OfType<JObject>().FirstOrDefault(e => string.Equals((string?)e["name"], name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>WEB-0072 D5 — the fail-closed access-map gate (pure decision, no host).</summary>
public sealed class AccessMapGateSpec
{
    private static ClaimsPrincipal User(string? role = null, string? scope = null)
    {
        var claims = new System.Collections.Generic.List<Claim> { new(ClaimTypes.NameIdentifier, "u") };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (scope is not null) claims.Add(new Claim("scope", scope));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    [Fact]
    public void Development_always_allows()
        => AccessMapGate.Allowed(isDevelopment: true, Anonymous, new McpExplorerOptions()).Should().BeTrue();

    [Fact]
    public void Production_fails_closed_for_anonymous_and_unconfigured()
    {
        AccessMapGate.Allowed(false, Anonymous, new McpExplorerOptions()).Should().BeFalse();
        // Authenticated but no admin gate configured → still denied (fail-closed).
        AccessMapGate.Allowed(false, User(role: "user"), new McpExplorerOptions()).Should().BeFalse();
    }

    [Fact]
    public void Production_allows_a_configured_admin_role_or_scope()
    {
        AccessMapGate.Allowed(false, User(role: "ops"), new McpExplorerOptions { AdminRole = "ops" }).Should().BeTrue();
        AccessMapGate.Allowed(false, User(scope: "mcp:admin"), new McpExplorerOptions { AdminScope = "mcp:admin" }).Should().BeTrue();
        // The wrong role/scope is denied.
        AccessMapGate.Allowed(false, User(role: "user"), new McpExplorerOptions { AdminRole = "ops" }).Should().BeFalse();
    }
}
