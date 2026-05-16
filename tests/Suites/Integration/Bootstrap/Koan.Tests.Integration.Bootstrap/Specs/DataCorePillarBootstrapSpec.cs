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
/// <b>Residual cross-pillar config:</b> after ARCH-0080 the cache adapter no longer registers
/// <c>IConnectionMultiplexer</c>, but <c>Koan.Data.Connector.Redis</c> (the canonical owner)
/// still does — and its factory eagerly calls <c>ConnectionMultiplexer.Connect()</c> which
/// throws if Redis isn't reachable. This spec supplies <c>abortConnect=false</c> on the
/// canonical key (per ARCH-0080) so the multiplexer is constructed in non-connected state.
/// The CoherenceCoordinator-tolerance fix then handles the downstream Subscribe failure
/// gracefully. A follow-up branch will make the data connector's factory tolerant on its
/// own — at which point this workaround disappears.
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
            // ARCH-0080 canonical key — see remarks on the class.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0,abortConnect=false,connectTimeout=100,syncTimeout=100")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Reference = Intent: with Koan.Data.Core + the InMemory connector referenced,
        // IDataService resolves without an explicit AddKoanDataCore() call.
        var data = host.Services.GetRequiredService<IDataService>();
        data.Should().NotBeNull();
    }
}
