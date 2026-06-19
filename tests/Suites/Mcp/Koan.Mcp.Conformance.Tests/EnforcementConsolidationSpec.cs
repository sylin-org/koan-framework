using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004 Phase 3.3b — MCP enforcement is split by tool kind, with NO per-transport duplication. An ENTITY
/// tool's authority is the data-layer <c>[Access]</c> gate (the same gate REST enforces, advertised over MCP via
/// <see cref="McpEntityGate"/> and enforced on call inside <c>CallToolFor</c>); a CUSTOM <c>[McpTool]</c> verb's
/// authority is the shared <see cref="McpToolAccessPolicy"/> scope filter (custom verbs have no entity/row, so
/// they keep the transport-edge check). STDIO is local-trust by design (the raw handler is unfiltered). These
/// specs pin all three: the custom-verb policy matrix, the entity gate's coarse decision, and STDIO local-trust.
/// </summary>
public sealed class EnforcementConsolidationSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public EnforcementConsolidationSpec(ConformanceFixture fx) => _fx = fx;

    // ---- Custom-verb authority: the policy matrix ------------------------------------------------

    [Fact]
    public void Custom_verb_policy_denies_an_unauthenticated_caller_when_auth_required()
    {
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: true, Array.Empty<string>()).Should().BeFalse();
        McpToolAccessPolicy.IsPermitted(User(), requiresAuth: true, Array.Empty<string>()).Should().BeTrue();
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: false, Array.Empty<string>()).Should().BeTrue();
    }

    [Fact]
    public void Custom_verb_required_scopes_are_enforced()
    {
        McpToolAccessPolicy.IsPermitted(User("vault:read"), requiresAuth: false, new[] { "vault:read" }).Should().BeTrue();
        McpToolAccessPolicy.IsPermitted(User("other"), requiresAuth: false, new[] { "vault:read" }).Should().BeFalse();
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: false, new[] { "vault:read" }).Should().BeFalse(
            "an anonymous caller presents no scopes");
        McpToolAccessPolicy.UserHasScopes(User("a", "b"), new[] { "a" }).Should().BeTrue();
        McpToolAccessPolicy.UserHasScopes(User("a"), new[] { "a", "b" }).Should().BeFalse("all required scopes must be held");
    }

    // ---- Entity-tool authority: the data-layer [Access] gate + STDIO local-trust tripwire ---------

    [Fact]
    public async Task Raw_handler_is_unfiltered_so_STDIO_is_local_trust()
    {
        // The raw McpRpcHandler (what STDIO binds) lists the scoped vault tool with NO principal — it does not
        // filter. That is the local-trust invariant: stdin/stdout = the local process owner.
        var tools = await _fx.ListToolsAsync();
        var vaultTool = tools.OfType<JObject>()
            .FirstOrDefault(t => (t["name"]?.Value<string>() ?? "").StartsWith("vault", StringComparison.OrdinalIgnoreCase));
        vaultTool.Should().NotBeNull("the raw handler lists the scoped vault tool unfiltered (local-trust)");

        // ...AND the entity [Access] gate WOULD deny an anonymous remote caller that same tool — proving the gate
        // is the entity-tool authority the remote edge advertises/enforces against, and STDIO bypasses it.
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        var gateCache = _fx.Services.GetRequiredService<IAccessGateCache>();
        registry.TryGetRegistration("vault", out var registration).Should().BeTrue();
        var tool = registration.Tools.First();

        McpEntityGate.CoarseAllows(gateCache, registration.EntityType, tool.Operation, Anon()).Should().BeFalse(
            "the data-layer gate denies an unscoped caller the scoped entity tool — the entity-tool authority");
    }

    private static ClaimsPrincipal Anon() => new(new ClaimsIdentity());

    private static ClaimsPrincipal User(params string[] scopes)
    {
        var claims = scopes.Length > 0 ? new[] { new Claim("scope", string.Join(' ', scopes)) } : Array.Empty<Claim>();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
