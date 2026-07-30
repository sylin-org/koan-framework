using System;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Crud;

public sealed class InMemoryCrudSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Upsert_query_count_and_remove_flow()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("crud");
        using var lease = Lease(partition);

        var saved = await Person.Upsert(new Person { Name = "Ada", Age = 34 });
        var originalTimestamp = saved.LastUpdated;
        saved.Id.Should().NotBeNullOrWhiteSpace();

        await Person.UpsertMany(new[]
        {
            new Person { Name = "Grace", Age = 47 },
            new Person { Name = "Bob", Age = 42 }
        });

        var all = await Person.All(partition);
        all.Should().HaveCount(3);

        var filtered = await Person.Query(p => p.Age > 40);
        filtered.Should().HaveCount(2);

        var updated = filtered.First();
        updated.Name = "Bobby";
        await Person.Upsert(updated);

        var fetched = await Person.Get(updated.Id);
        fetched!.Name.Should().Be("Bobby");
        fetched.LastUpdated.Should().NotBe(originalTimestamp);

        var count = await Person.Count.Where(p => p.Age >= 40, CountStrategy.Exact);
        count.Should().Be(2);

        var removed = await Person.Remove(saved.Id, partition);
        removed.Should().BeTrue();

        var remaining = await Person.All(partition);
        remaining.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stored_values_are_detached_snapshots_until_saved_again()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var lease = Lease(NewPartition("snapshots"));

        var original = new SnapshotEntity
        {
            Name = "stored",
            Detail = new SnapshotDetail { Count = 2 },
            Tags = ["one", "two"]
        };
        await SnapshotEntity.Upsert(original);

        original.Name = "caller-mutated";
        original.Detail.Count = 9;
        original.Tags.Add("three");

        var first = (await SnapshotEntity.Get(original.Id))!;
        first.Should().NotBeSameAs(original);
        first.Name.Should().Be("stored");
        first.Detail.Count.Should().Be(2);
        first.Tags.Should().Equal("one", "two");

        first.Name = "read-mutated";
        first.Detail.Count = 7;
        first.Tags.Clear();

        var queryValue = (await SnapshotEntity.Query(item => item.Id == original.Id)).Single();
        queryValue.Should().NotBeSameAs(first);
        queryValue.Name.Should().Be("stored");
        queryValue.Detail.Count.Should().Be(2);
        queryValue.Tags.Should().Equal("one", "two");

        queryValue.Name = "query-mutated";
        (await SnapshotEntity.Get(original.Id))!.Name.Should().Be("stored");
    }

    [Fact]
    public async Task Variant_round_trips_through_its_root_as_a_detached_snapshot()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var lease = Lease(NewPartition("variants"));

        var original = new InMemoryLibraryItem.Book
        {
            Title = "Koan",
            Pages = 320,
            Related = new InMemoryLibraryItem.Video { Title = "Walkthrough", Minutes = 18 }
        };
        await InMemoryLibraryItem.Book.Upsert(original);

        var loaded = await InMemoryLibraryItem.Get(original.Id);
        var book = loaded.Should().BeOfType<InMemoryLibraryItem.Book>().Which;
        book.Should().NotBeSameAs(original);
        book.Pages.Should().Be(320);
        book.Related.Should().BeOfType<InMemoryLibraryItem.Video>().Which.Minutes.Should().Be(18);

        book.Title = "unsaved";
        (await InMemoryLibraryItem.Get(original.Id))!.Title.Should().Be("Koan");
    }

    private sealed class Person : Entity<Person>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        [Timestamp(OnSave = true)]
        public DateTimeOffset LastUpdated { get; set; }
    }

    public sealed class SnapshotEntity : Entity<SnapshotEntity>
    {
        public string Name { get; set; } = "";
        public SnapshotDetail Detail { get; set; } = new();
        public List<string> Tags { get; set; } = [];
    }

    public sealed class SnapshotDetail
    {
        public int Count { get; set; }
    }

}

public class InMemoryLibraryItem : Entity<InMemoryLibraryItem>
{
    public string Title { get; set; } = "";
    public InMemoryLibraryItem? Related { get; set; }

    public sealed class Book : InMemoryLibraryItem<Book>
    {
        public int Pages { get; set; }
    }

    public sealed class Video : InMemoryLibraryItem<Video>
    {
        public int Minutes { get; set; }
    }
}
