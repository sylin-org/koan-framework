using System;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Data pillar core (per ARCH-0079). Proves <c>IDataService</c> resolves
/// end-to-end through real <c>AddKoan()</c> reflective discovery with the InMemory connector.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cross-pillar coupling note:</b> the bootstrap test project transitively references
/// Redis adapters via the cache pillar smoke. This spec sets <c>abortConnect=false</c> on
/// the Redis connection string so the <c>IConnectionMultiplexer</c> factory doesn't throw at
/// construction time. The <c>CoherenceCoordinator</c> degrade-on-subscribe-failure fix
/// (commit on this branch) then handles the post-construction Subscribe timeout gracefully —
/// the host comes up even though no Redis is running. ARCH-0080 will eliminate this workaround
/// by making the cache adapter consume rather than re-register the multiplexer.
/// </para>
/// </remarks>
public sealed class DataCorePillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public DataCorePillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IDataService_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Environment", "Test")
            // Cross-pillar workaround — see remarks on the class.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0,abortConnect=false,connectTimeout=100,syncTimeout=100")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Reference = Intent: with Koan.Data.Core + the InMemory connector referenced,
        // IDataService resolves without an explicit AddKoanDataCore() call.
        var data = host.Services.GetRequiredService<IDataService>();
        data.Should().NotBeNull();
    }
}
