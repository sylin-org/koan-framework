using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Mcp;
using Koan.Mcp.Execution;
using Koan.Mcp.Options;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN3 (docs/assessment/09 §8) — the MCP effective-access decision is ONE shared policy
/// (<see cref="McpToolAccessPolicy"/>), not a per-transport copy. The remote HTTP/SSE edge consults it;
/// STDIO is local-trust by design (the raw handler is unfiltered). These specs pin both halves: the
/// policy's auth/scope matrix (the single authority) and the STDIO local-trust invariant.
/// </summary>
public sealed class EnforcementConsolidationSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public EnforcementConsolidationSpec(ConformanceFixture fx) => _fx = fx;

    // ---- The single authority: the policy matrix -------------------------------------------------

    [Fact]
    public void Unauthenticated_caller_is_denied_when_auth_required()
    {
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: true, Array.Empty<string>()).Should().BeFalse();
        McpToolAccessPolicy.IsPermitted(User(), requiresAuth: true, Array.Empty<string>()).Should().BeTrue();
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: false, Array.Empty<string>()).Should().BeTrue();
    }

    [Fact]
    public void Required_scopes_are_enforced()
    {
        McpToolAccessPolicy.IsPermitted(User("vault:read"), requiresAuth: false, new[] { "vault:read" }).Should().BeTrue();
        McpToolAccessPolicy.IsPermitted(User("other"), requiresAuth: false, new[] { "vault:read" }).Should().BeFalse();
        McpToolAccessPolicy.IsPermitted(Anon(), requiresAuth: false, new[] { "vault:read" }).Should().BeFalse(
            "an anonymous caller presents no scopes");
        McpToolAccessPolicy.UserHasScopes(User("a", "b"), new[] { "a" }).Should().BeTrue();
        McpToolAccessPolicy.UserHasScopes(User("a"), new[] { "a", "b" }).Should().BeFalse("all required scopes must be held");
    }

    // ---- STDIO local-trust tripwire --------------------------------------------------------------

    [Fact]
    public async Task Raw_handler_is_unfiltered_so_STDIO_is_local_trust()
    {
        // The raw McpRpcHandler (what STDIO binds) lists a scoped tool with NO principal — it does not
        // apply the policy. That is the local-trust invariant: stdin/stdout = the local process owner.
        var tools = await _fx.ListToolsAsync();
        var vaultTool = tools.OfType<JObject>()
            .FirstOrDefault(t => (t["name"]?.Value<string>() ?? "").StartsWith("vault", StringComparison.OrdinalIgnoreCase));
        vaultTool.Should().NotBeNull("the raw handler lists the scoped vault tool unfiltered (local-trust)");

        // ...AND the shared policy WOULD deny an anonymous caller for that same tool — proving the policy
        // is the authority the remote edge enforces, and STDIO deliberately bypasses it.
        var registry = _fx.Services.GetRequiredService<McpEntityRegistry>();
        var options = _fx.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        registry.TryGetRegistration("vault", out var registration).Should().BeTrue();
        var tool = registration.Tools.First();

        McpToolAccessPolicy.IsEntityToolPermitted(Anon(), registration, tool, options).Should().BeFalse(
            "the remote enforcement policy denies an unscoped caller the scoped tool");
    }

    private static ClaimsPrincipal Anon() => new(new ClaimsIdentity());

    private static ClaimsPrincipal User(params string[] scopes)
    {
        var claims = scopes.Length > 0 ? new[] { new Claim("scope", string.Join(' ', scopes)) } : Array.Empty<Claim>();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
