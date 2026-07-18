using System;
using System.Linq;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Crud;

public sealed class PostgresCrudSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task Raw_predicates_do_not_invent_a_default_page()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-pagination-intent"));

        await Person.UpsertMany(Enumerable.Range(1, 75)
            .Select(age => new Person { Name = $"Person-{age}", Age = age }));

        var all = await Data<Person, string>.QueryRaw("1 = 1");
        var page = await Data<Person, string>.QueryRaw(
            "1 = 1",
            shaping: QueryDefinition.All.WithPagination(page: 2, pageSize: 7));

        all.Should().HaveCount(75);
        page.Should().HaveCount(7);
    }

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
            new Person { Name = "Bob", Age = 42 },
            new Person { Name = "Edsger", Age = 59 }
        });

        var all = await Person.All(partition);
        all.Should().HaveCount(4);

        var filtered = await Data<Person, string>.Query(p => p.Age >= 40, partition);
        filtered.Should().HaveCount(3);

        var updated = filtered.First(f => f.Name == "Bob");
        updated.Name = "Bobby";
        updated.Age = 43;
        await Person.Upsert(updated);

        var fetched = await Person.Get(updated.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Bobby");
        fetched.LastUpdated.Should().BeOnOrAfter(originalTimestamp);

        var count = await Data<Person, string>.Count(p => p.Age >= 40, partition);
        count.Should().Be(3);

        var page = await Person.Page(1, 2);
        page.Should().HaveCount(2);

        var removed = await Person.Remove(saved.Id, partition);
        removed.Should().BeTrue();

        var remainingIds = filtered.Select(p => p.Id).Skip(1).ToArray();
        var removedMany = await Person.Remove(remainingIds);
        removedMany.Should().Be(2);

        var remaining = await Person.All(partition);
        remaining.Should().HaveCount(1);
        remaining[0].Name.Should().Be("Grace");
    }

    private sealed class Person : Entity<Person>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        [Timestamp(OnSave = true)]
        public DateTimeOffset LastUpdated { get; set; }
    }
}
