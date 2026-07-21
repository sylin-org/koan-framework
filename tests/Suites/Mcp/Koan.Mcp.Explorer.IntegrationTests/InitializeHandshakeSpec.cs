using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>
/// WEB-0072 P2 — the MCP <c>initialize</c> handshake (an MCP-core addition). It must advertise the protocol
/// version + capabilities and, crucially, the server identity (<c>serverInfo</c>) and the <c>instructions</c>
/// the LLM reads — sourced declare-once from <c>[KoanApp]</c> / <c>Koan:Mcp:Instructions</c>.
/// </summary>
public sealed class InitializeHandshakeSpec : IClassFixture<ExplorerFixture>
{
    private readonly ExplorerFixture _fx;
    public InitializeHandshakeSpec(ExplorerFixture fx) => _fx = fx;

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_advertises_serverInfo_capabilities_and_instructions()
    {
        var handler = _fx.Services.GetRequiredService<McpServer>().CreateHandler();

        var result = await handler.Initialize(new McpRpcHandler.InitializeParams { ProtocolVersion = "2025-06-18" }, Ct);

        result.ProtocolVersion.Should().Be("2025-06-18");
        result.ServerInfo.Name.Should().NotBeNullOrEmpty();
        result.ServerInfo.Version.Should().NotBeNullOrEmpty();
        result.Capabilities.Should().NotBeNull();
        result.Capabilities["tools"].Should().NotBeNull();
        result.Capabilities["resources"].Should().NotBeNull();

        // The LLM-facing guidance flows from configuration (declare-once).
        result.Instructions.Should().Be(ExplorerFixture.TestInstructions);
    }

    [Fact]
    public async Task Initialize_defaults_the_protocol_version_when_the_client_omits_it()
    {
        var handler = _fx.Services.GetRequiredService<McpServer>().CreateHandler();
        var result = await handler.Initialize(null, Ct);
        result.ProtocolVersion.Should().Be(McpRpcHandler.DefaultProtocolVersion);
    }
}
