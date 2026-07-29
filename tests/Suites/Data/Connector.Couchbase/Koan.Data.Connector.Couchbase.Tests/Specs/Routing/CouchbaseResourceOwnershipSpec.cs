using Koan.Data.Connector.Couchbase.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Routing;

public sealed class CouchbaseResourceOwnershipSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchbaseFixture>(fixture, output)
{
    [Fact]
    public async Task Multiple_entity_containers_share_one_host_owned_cluster()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("resource-owner"));

        await new FirstDocument { Value = "first" }.Save();
        await new SecondDocument { Value = "second" }.Save();

        host.Services.GetRequiredService<CouchbaseResourcePool>()
            .ClusterCount.Should().Be(1);
    }

    private sealed class FirstDocument : Entity<FirstDocument>
    {
        public string Value { get; set; } = "";
    }

    private sealed class SecondDocument : Entity<SecondDocument>
    {
        public string Value { get; set; } = "";
    }
}
