using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI.Contracts;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the AI pillar (per ARCH-0079). Proves <c>IAiPipeline</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// The AI auto-registrar wires <c>AiSourceHealthMonitor</c> as a hosted service that
/// probes HTTP endpoints. With no adapter contributors registered in this test, no
/// adapters get added and the probe stays idle — safe for offline bootstrap.
/// </remarks>
public sealed class AiPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public AiPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IAiPipeline_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var pipeline = host.Services.GetRequiredService<IAiPipeline>();
        pipeline.Should().NotBeNull();
    }
}
