using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the Data pillar core (per ARCH-0079). Proves <c>IDataService</c> resolves
/// end-to-end through real <c>AddKoan()</c> reflective discovery with the InMemory connector.
/// </summary>
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
        await using var host = await PillarHost.Configure()
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Environment", "Test")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Reference = Intent: with Koan.Data.Core + the InMemory connector referenced,
        // IDataService resolves without an explicit AddKoanDataCore() call.
        var data = host.Services.GetRequiredService<IDataService>();
        data.Should().NotBeNull();
    }
}
