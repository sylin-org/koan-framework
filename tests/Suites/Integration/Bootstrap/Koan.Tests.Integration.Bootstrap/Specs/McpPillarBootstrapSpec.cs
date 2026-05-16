using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core;
using Koan.Mcp.Hosting;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Mcp pillar (per ARCH-0079). Proves <c>McpServer</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <para>
/// The Mcp pillar registers three hosted services: <c>StdioTransport</c>,
/// <c>HttpSseSessionManager</c>, and <c>WebSocketTransport</c>. <c>StdioTransport</c>'s
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
