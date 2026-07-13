using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Proves that a second real Koan host can use the same closed-generic Entity path after the first
/// host and its service provider have been disposed.
/// </summary>
[Collection(nameof(DataAiHostLifecycleCollection))]
public sealed class RepeatedHostLifecycleSpecs
{
    [Fact]
    public async Task Sequential_hosts_do_not_share_provider_or_entity_storage()
    {
        const string EntityId = "shared-id";

        var first = await StartHost("memory://repeated-host-a");
        try
        {
            AppHost.Current.Should().NotBeNull();
            AppHost.Current!.GetRequiredService<RepeatedHostMarker>().Name.Should().Be("memory://repeated-host-a");
            await new RepeatedHostEntity { Id = EntityId, Value = "host-a" }.Save();
            (await RepeatedHostEntity.Get(EntityId))!.Value.Should().Be("host-a");
        }
        finally
        {
            await first.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();

        var second = await StartHost("memory://repeated-host-b");
        try
        {
            AppHost.Current.Should().NotBeNull();
            AppHost.Current!.GetRequiredService<RepeatedHostMarker>().Name.Should().Be("memory://repeated-host-b");
            (await RepeatedHostEntity.Get(EntityId)).Should().BeNull();

            await new RepeatedHostEntity { Id = EntityId, Value = "host-b" }.Save();
            (await RepeatedHostEntity.Get(EntityId))!.Value.Should().Be("host-b");
        }
        finally
        {
            await second.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
    }

    private static Task<IntegrationHost> StartHost(string connectionString)
        => KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", connectionString)
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RepeatedHostMarker(connectionString));
                services.AddLogging();
                services.AddKoan();
            })
            .StartAsync();
}
