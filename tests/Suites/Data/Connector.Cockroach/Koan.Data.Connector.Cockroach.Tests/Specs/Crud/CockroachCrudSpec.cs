using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.Cockroach.Tests.Specs.Crud;

public sealed class CockroachCrudSpec(CockroachFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CockroachFixture>(fixture, output)
{
    [Fact]
    public async Task Raw_predicates_preserve_explicit_paging_intent()
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
    public async Task Upsert_query_count_update_and_remove_form_one_ordinary_flow()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("crud");
        using var lease = Lease(partition);

        var saved = await Person.Upsert(new Person { Name = "Ada", Age = 34 });
        saved.Id.Should().NotBeNullOrWhiteSpace();
        var originalTimestamp = saved.LastUpdated;

        await Person.UpsertMany([
            new Person { Name = "Grace", Age = 47 },
            new Person { Name = "Bob", Age = 42 },
            new Person { Name = "Edsger", Age = 59 }
        ]);

        (await Person.All(partition)).Should().HaveCount(4);
        var filtered = await Data<Person, string>.Query(person => person.Age >= 40, partition);
        filtered.Should().HaveCount(3);

        var updated = filtered.First(person => person.Name == "Bob");
        updated.Name = "Bobby";
        updated.Age = 43;
        await Person.Upsert(updated);

        var fetched = await Person.Get(updated.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Bobby");
        fetched.LastUpdated.Should().BeOnOrAfter(originalTimestamp);
        (await Data<Person, string>.Count(person => person.Age >= 40, partition)).Should().Be(3);
        (await Person.Page(1, 2)).Should().HaveCount(2);

        (await Person.Remove(saved.Id, partition)).Should().BeTrue();
        var remainingIds = filtered
            .Where(person => person.Name != "Grace")
            .Select(person => person.Id)
            .ToArray();
        (await Person.Remove(remainingIds)).Should().Be(2);
        (await Person.All(partition)).Should().ContainSingle()
            .Which.Name.Should().Be("Grace");
    }

    private sealed class Person : Entity<Person>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset LastUpdated { get; set; }
    }
}
