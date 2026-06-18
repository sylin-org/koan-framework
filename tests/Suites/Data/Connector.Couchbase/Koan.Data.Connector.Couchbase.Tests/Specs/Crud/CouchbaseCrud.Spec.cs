using System;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Crud;

public sealed class CouchbaseCrudSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchbaseFixture>(fixture, output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Upsert_query_count_and_remove_flow()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("crud");
        using var lease = Lease(partition);

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

        var removed = await CouchbaseProduct.Remove(saved.Id, partition);
        removed.Should().BeTrue();

        var remaining = await CouchbaseProduct.All(partition);
        remaining.Should().HaveCount(2);
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
