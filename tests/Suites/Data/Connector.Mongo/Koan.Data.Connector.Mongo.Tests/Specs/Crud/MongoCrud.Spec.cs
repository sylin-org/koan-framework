using System;
using System.Linq;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Crud;

public sealed class MongoCrudSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Upsert_query_count_and_remove_flow()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("crud");
        using var lease = Lease(partition);

        var saved = await Person.Upsert(new Person { Name = "Ada", Age = 34 });
        saved.Id.Should().NotBeNullOrWhiteSpace();
        var originalTimestamp = saved.LastUpdated;

        await Person.UpsertMany(new[]
        {
            new Person { Name = "Grace", Age = 47 },
            new Person { Name = "Bob", Age = 42 }
        });

        var all = await Person.All(partition);
        all.Should().HaveCount(3);

        var filtered = await Data<Person, string>.Query(p => p.Name != "Ada", partition);
        filtered.Should().HaveCount(2);

        var updated = filtered.First();
        updated.Name = "Bobby";
        await Person.Upsert(updated);

        var fetched = await Person.Get(updated.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Bobby");
        fetched.LastUpdated.Should().NotBe(originalTimestamp);

        var count = await Data<Person, string>.Count(p => p.Name != "Ada", partition);
        count.Should().Be(2);

        var removed = await Person.Remove(saved.Id, partition);
        removed.Should().BeTrue();

        var remaining = await Person.All(partition);
        remaining.Should().HaveCount(2);
    }

    private sealed class Person : Entity<Person>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset LastUpdated { get; set; }
    }
}
