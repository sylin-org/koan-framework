using System;
using System.Threading.Tasks;
using AwesomeAssertions;
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
/// Points at <c>localhost:0</c> (invalid port) so the spec stays offline and never
/// reaches a real Redis on the dev box. The connector's <c>IConnectionMultiplexer</c>
/// factory is tolerant by default (ARCH-0080 follow-up) — it constructs in disconnected
/// state without throwing, and the CoherenceCoordinator handles the downstream Subscribe
/// failure gracefully.
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
            // Offline-only — invalid port keeps the multiplexer from reaching real Redis.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Reference = Intent: with Koan.Data.Core + the InMemory connector referenced,
        // IDataService resolves without an explicit AddKoanDataCore() call.
        var data = host.Services.GetRequiredService<IDataService>();
        data.Should().NotBeNull();
    }
}
