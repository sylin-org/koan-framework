using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.AI;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Proves that a second real Koan host can use the same closed-generic Entity and AI registry paths
/// after the first host and its service provider have been disposed.
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

    [Fact]
    public async Task Sequential_hosts_record_vector_model_confirmation_in_their_own_backend()
    {
        const string Model = "shared-embedding-model";

        var first = await StartHost("memory://vector-model-host-a");
        try
        {
            await VectorModelGuard.GuardWrite<RepeatedHostEntity>(Model);
            (await VectorModelGuard.ModelsInIndex<RepeatedHostEntity>()).Should().Equal(Model);
        }
        finally
        {
            await first.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();

        var second = await StartHost("memory://vector-model-host-b");
        try
        {
            (await VectorModelGuard.ModelsInIndex<RepeatedHostEntity>()).Should().BeEmpty();

            await VectorModelGuard.GuardWrite<RepeatedHostEntity>(Model);

            (await VectorModelGuard.ModelsInIndex<RepeatedHostEntity>()).Should().Equal(Model);
        }
        finally
        {
            await second.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Sequential_hosts_register_one_embedding_lifecycle_handler()
    {
        EmbeddingRegistry.RegisterTypes([typeof(RepeatedHostEmbeddingEntity)]);

        var first = await StartHost("memory://lifecycle-handler-host-a");
        try
        {
            LifecycleInfo(first).HandlerCounts["after-upsert"].Should().Be(1);
        }
        finally
        {
            await first.DisposeAsync();
        }

        var second = await StartHost("memory://lifecycle-handler-host-b");
        try
        {
            LifecycleInfo(second).HandlerCounts["after-upsert"].Should().Be(1,
                "each host owns one AI lifecycle contribution without process accumulation");
        }
        finally
        {
            await second.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
    }

    private static Koan.Data.Core.Lifecycle.EntityLifecycleInfo LifecycleInfo(IntegrationHost host)
        => host.Services.GetRequiredService<IDataDiagnostics>()
            .GetLifecyclePlansSnapshot()
            .Single(info => info.EntityType.EndsWith(nameof(RepeatedHostEmbeddingEntity), StringComparison.Ordinal));

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
