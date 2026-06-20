using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Mcp.Hosting;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Mcp pillar (per ARCH-0079). Proves <c>McpServer</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <para>
/// The Mcp pillar registers two hosted services: <c>StdioTransport</c> and the unified
/// <c>McpSessionManager</c> (AI-0037 Ph3b collapsed the legacy <c>HttpSseSessionManager</c>
/// onto it). <c>StdioTransport</c>'s
/// <c>ExecuteAsync</c> checks <c>EnableStdioTransport</c> AND the registered-entity count;
/// with neither set in a clean test bootstrap, it short-circuits to "disabled / idle"
/// without touching <c>Console.In</c>. Safe offline.
/// </para>
/// </remarks>
public sealed class McpPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public McpPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_McpServer_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Explicit-off defends future StdioTransport behavioural changes.
            .WithSetting("Koan:Mcp:EnableStdioTransport", "false")
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var server = host.Services.GetRequiredService<McpServer>();
        server.Should().NotBeNull();
    }
}
