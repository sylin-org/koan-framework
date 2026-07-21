using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots an MCP host exposing ONLY <see cref="Lockbox"/> (an <c>origin:local</c>-gated entity).</summary>
public sealed class OriginEntityFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("lockbox");
}

/// <summary>
/// SEC-0004 origin (ARCH-0092 Phase 3.3) — the transport-trust dimension enforced end-to-end over MCP. The STDIO
/// raw handler stamps <c>origin:local</c>, so it may read a <c>[Access(read: "origin:local")]</c> entity; a remote
/// caller (the request builder defaults an un-stamped, non-HTTP principal to <c>origin:remote</c>) is denied. Code
/// mode is held to the same gate — local over STDIO, remote otherwise — so it cannot out-privilege the transport.
/// </summary>
public sealed class OriginGateSpec : IClassFixture<OriginEntityFixture>
{
    private readonly OriginEntityFixture _fx;

    public OriginGateSpec(OriginEntityFixture fx) => _fx = fx;

    private string ReadTool => _fx.ResolveToolName("lockbox", EntityEndpointOperationKind.Collection);

    [Fact]
    public async Task A_stdio_caller_is_local_and_may_read_an_origin_local_entity()
    {
        // CallToolAsync goes through the raw McpRpcHandler (the STDIO path), which stamps origin:local.
        var result = await _fx.CallToolAsync(ReadTool, new JObject());
        McpHarnessFixtureBase.IsShortCircuited(result).Should().BeFalse(
            "the STDIO raw handler stamps origin:local — the gate admits the read");
    }

    [Fact]
    public async Task A_remote_caller_is_denied_an_origin_local_entity()
    {
        // CallToolFor with an un-stamped principal → the request builder fails safe to origin:remote.
        var result = await _fx.CallToolAsAsync(ReadTool, new JObject(), McpHarnessFixtureBase.Principal(scopes: "anything"));
        McpHarnessFixtureBase.IsShortCircuited(result).Should().BeTrue(
            "a remote caller is not local — origin:local denies it (transport-trust ≠ identity)");
    }

    [Fact]
    public async Task Code_mode_is_held_to_the_same_origin_gate()
    {
        // Seed one row — write is open, only read is origin-gated.
        var upsert = _fx.ResolveToolName("lockbox", EntityEndpointOperationKind.Upsert);
        await _fx.CallToolAsync(upsert, new JObject { ["model"] = new JObject { ["contents"] = "gold" } });

        const string code = "SDK.Out.answer(JSON.stringify(SDK.Entities.lockbox.collection()));";

        // STDIO code mode runs as local → the sandbox read passes origin:local and sees the row.
        var local = await _fx.CallToolAsync("koan.code.execute", new JObject { ["code"] = code });
        McpHarnessFixtureBase.ContentText(local).Should().Contain("gold",
            "code mode over STDIO is local — origin:local admits the sandbox read");

        // Remote code mode is gate-denied the local-only read → it never sees the row (not a bypass).
        var remote = await _fx.CallToolAsAsync("koan.code.execute", new JObject { ["code"] = code },
            McpHarnessFixtureBase.Principal(scopes: "anything"));
        McpHarnessFixtureBase.ContentText(remote).Should().NotContain("gold",
            "a remote sandbox is denied the local-only read — origin gates code mode too");
    }
}
