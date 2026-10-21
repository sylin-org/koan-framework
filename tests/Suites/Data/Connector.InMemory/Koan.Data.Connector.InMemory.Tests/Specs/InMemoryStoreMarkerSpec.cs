using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs;

/// <summary>
/// DATA-0148: each InMemory source keys its own host-owned store, and holds one marker beside it. Two sources read two
/// markers, one source reads one, and a ReadOnly source never creates it.
/// </summary>
public sealed class InMemoryStoreMarkerSpec
{
    [Fact]
    public async Task Each_source_holds_one_marker_and_a_ReadOnly_source_never_creates_it()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Archive:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Reader:Adapter"] = "inmemory",
                ["Koan:Data:Sources:Reader:Access"] = "ReadOnly"
            })
            .ConfigureServices(static services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
        var factory = host.Services.GetServices<IDataAdapterFactory>().Single(static f => f.Provider == "inmemory");
        var ct = TestContext.Current.CancellationToken;

        var marker = await factory.ReadStoreMarker(host.Services, "Default", create: true, ct);

        marker.Should().NotBeNullOrWhiteSpace();
        (await factory.ReadStoreMarker(host.Services, "Default", create: false, ct)).Should().Be(marker);
        (await factory.ReadStoreMarker(host.Services, "Archive", create: true, ct)).Should().NotBe(marker);
        (await factory.ReadStoreMarker(host.Services, "Reader", create: true, ct)).Should().BeNull();
    }
}
