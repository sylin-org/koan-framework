using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using AwesomeAssertions;
using Koan.Mcp.Execution;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0006 — granted OAuth scopes must reach the claim the access gates read. A consented scope used to land in
/// the inert <c>Koan.permission</c> claim, but both the SEC-0004 <c>[Access(has:scope:x)]</c> gate and the custom
/// <c>[McpTool(RequiredScopes)]</c> policy (<see cref="McpToolAccessPolicy"/>) read the RFC 9068 <c>scope</c> claim
/// — so a scope-gated verb could never be satisfied by an AS-issued token. This proves the AS-issued token now
/// satisfies that gate, and that the dev-token can request arbitrary scopes for a local test loop.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class ScopeClaimSpec : IClassFixture<McpAuthRampFixture>
{
    private readonly McpAuthRampFixture _fx;
    public ScopeClaimSpec(McpAuthRampFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    [Fact]
    public async Task DevToken_minted_scopes_satisfy_the_mcp_custom_tool_gate()
    {
        using var client = _fx.NewClient();
        (await client.GetAsync("/test/signin", Ct)).EnsureSuccessStatusCode();

        // #2 — the dev-token requests arbitrary scopes (Development-only; minted as-is).
        var res = await client.GetAsync("/oauth/dev-token?scope=orders:read%20orders:fulfill", Ct);
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("scope").GetString().Should().Contain("orders:fulfill");
        var token = doc.RootElement.GetProperty("access_token").GetString()!;

        // The token a resource server / the MCP edge sees: the `scope` claim is present and gate-readable.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test"));

        // #1 — the granted scope now reaches the custom-[McpTool] gate (it was inert in Koan.permission before).
        McpToolAccessPolicy.UserHasScopes(principal, new[] { "orders:fulfill" }).Should().BeTrue();
        McpToolAccessPolicy.UserHasScopes(principal, new[] { "orders:read", "orders:fulfill" }).Should().BeTrue();
        // A scope that was NOT granted is denied.
        McpToolAccessPolicy.UserHasScopes(principal, new[] { "admin:purge" }).Should().BeFalse();
    }
}
