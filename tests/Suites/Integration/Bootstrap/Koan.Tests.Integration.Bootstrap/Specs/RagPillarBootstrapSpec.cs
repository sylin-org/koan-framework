using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core;
using Koan.Rag.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Rag pillar (per ARCH-0079). Proves <c>IRagService</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <c>RagIngestionWorker</c> is hosted and polls. The reflection scan for
/// <c>[RagCorpus]</c>-decorated entities returns an empty set in a clean bootstrap test —
/// the worker stays idle. Safe offline.
/// </remarks>
public sealed class RagPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public RagPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IRagService_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var rag = host.Services.GetRequiredService<IRagService>();
        rag.Should().NotBeNull();
    }
}
