using System;
using System.Linq;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Couchbase.Tests.Support;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Crud;

public sealed class CouchbaseCrudSpec
{
    private readonly ITestOutputHelper _output;

    public CouchbaseCrudSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Upsert_query_count_and_remove_flow()
    {
        await TestPipeline.For<CouchbaseCrudSpec>(_output, nameof(Upsert_query_count_and_remove_flow))
            .RequireDocker()
            .Using<CouchbaseContainerFixture>("couchbase", _ => ValueTask.FromResult(new CouchbaseContainerFixture()), (ctx, fixture) =>
            {
                ctx.Diagnostics.Info("couchbase.fixture.ready", new
                {
                    available = fixture.IsAvailable,
                    connectionString = fixture.ConnectionString,
                    reason = fixture.UnavailableReason
                });
            })
            .Using<CouchbaseConnectorFixture>("fixture", static ctx => CouchbaseConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<CouchbaseConnectorFixture>("fixture");
                await fixture.ResetAsync<CouchbaseProduct, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<CouchbaseConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                var saved = await CouchbaseProduct.Upsert(new CouchbaseProduct { Name = "Widget", Price = 9.99m, Category = "Hardware" });
                saved.Id.Should().NotBeNullOrWhiteSpace();

                await CouchbaseProduct.UpsertMany(
                [
                    new CouchbaseProduct { Name = "Gadget", Price = 24.50m, Category = "Electronics" },
                    new CouchbaseProduct { Name = "Sprocket", Price = 3.75m, Category = "Hardware" }
                ]);

                var all = await CouchbaseProduct.All(partition);
                all.Should().HaveCount(3);

                var filtered = await Data<CouchbaseProduct, string>.Query(p => p.Category == "Hardware", partition);
                filtered.Should().HaveCount(2);

                var count = await Data<CouchbaseProduct, string>.Count(p => p.Category == "Hardware", partition);
                count.Should().Be(2);

                var removed = await CouchbaseProduct.Remove(saved.Id, new DataQueryOptions { Partition = partition });
                removed.Should().BeTrue();

                var remaining = await CouchbaseProduct.All(partition);
                remaining.Should().HaveCount(2);
            })
            .Run();
    }
}

internal sealed class CouchbaseProduct : Entity<CouchbaseProduct>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";

    [Timestamp(OnSave = true)]
    public DateTimeOffset LastUpdated { get; set; }
}
