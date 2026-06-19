using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots an MCP host exposing ONLY <see cref="Coffer"/> (an <c>[Access]</c>-gated entity), so the
/// principal-threading enforcement specs resolve its tools unambiguously.</summary>
public sealed class GatedEntityFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("coffer");
}

/// <summary>
/// SEC-0004 Phase 3.3 — the MCP caller's principal is threaded into <c>EntityRequestContext.User</c>, so the
/// data-layer gate enforces the entity <c>[Access]</c> on the MCP surface identically to REST: a caller holding
/// <c>coffer:read</c> is allowed, one without is denied, and an anonymous caller (the STDIO default) is denied.
/// Before the threading, every MCP call ran anonymous — so even the scoped caller was denied; this is the proof
/// the principal now flows.
/// </summary>
public sealed class PrincipalThreadingSpec : IClassFixture<GatedEntityFixture>
{
    private readonly GatedEntityFixture _fx;

    public PrincipalThreadingSpec(GatedEntityFixture fx) => _fx = fx;

    private string ReadTool => _fx.ResolveToolName("coffer", EntityEndpointOperationKind.Collection);

    [Fact]
    public async Task An_authenticated_caller_with_the_scope_is_allowed()
    {
        var result = await _fx.CallToolAsAsync(ReadTool, new JObject(), McpHarnessFixtureBase.Principal(scopes: "coffer:read"));
        McpHarnessFixtureBase.IsShortCircuited(result).Should().BeFalse(
            "the data-layer gate allows a caller holding coffer:read — the threaded principal carries the scope");
    }

    [Fact]
    public async Task An_authenticated_caller_without_the_scope_is_denied()
    {
        var result = await _fx.CallToolAsAsync(ReadTool, new JObject(), McpHarnessFixtureBase.Principal(scopes: "other:read"));
        McpHarnessFixtureBase.IsShortCircuited(result).Should().BeTrue(
            "the gate forbids a caller lacking coffer:read — now enforced at the data layer over MCP, not just at the transport edge");
    }

    [Fact]
    public async Task An_anonymous_caller_is_denied()
    {
        // The raw-handler path (STDIO-equivalent) runs anonymous by default — gated data needs an identity.
        var result = await _fx.CallToolAsync(ReadTool, new JObject());
        McpHarnessFixtureBase.IsShortCircuited(result).Should().BeTrue(
            "anonymous is challenged by the gate: STDIO is local-trust for tool DISCOVERY, but gated DATA still needs a principal");
    }

    [Fact]
    public async Task Code_mode_entity_calls_run_as_the_caller_principal()
    {
        // Seed one coffer (write is open — only read is gated), then read it from INSIDE the sandbox.
        var upsert = _fx.ResolveToolName("coffer", EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(upsert, new JObject { ["model"] = new JObject { ["contents"] = "treasure" } });

        const string code = "SDK.Out.answer(JSON.stringify(SDK.Entities.coffer.collection()));";

        // Authed with the scope: the sandbox's read passes the gate → the seeded row is visible.
        var authed = await _fx.CallToolAsAsync("koan.code.execute", new JObject { ["code"] = code }, McpHarnessFixtureBase.Principal(scopes: "coffer:read"));
        McpHarnessFixtureBase.ContentText(authed).Should().Contain("treasure",
            "the principal threads into the sandbox — the code runs AS the scoped caller and sees the row");

        // Anonymous: the sandbox's read is gate-denied → it sees an empty collection, never the row.
        var anon = await _fx.CallToolAsync("koan.code.execute", new JObject { ["code"] = code });
        McpHarnessFixtureBase.ContentText(anon).Should().NotContain("treasure",
            "an anonymous sandbox is gate-denied the read — code mode is not a bypass of the data-layer gate");
    }
}
