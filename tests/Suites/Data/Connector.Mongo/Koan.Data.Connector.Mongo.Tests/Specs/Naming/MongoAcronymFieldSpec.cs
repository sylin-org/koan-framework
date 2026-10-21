namespace Koan.Data.Connector.Mongo.Tests.Specs.Naming;

/// <summary>
/// The serializer lower-cases a whole leading capital run (ISBN is stored as "isbn", IPAddress as "ipAddress"), so a
/// filter or sort that lower-cases only the first letter addresses a field no document holds (NK-029).
/// </summary>
public sealed class MongoAcronymFieldSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    private static readonly Book[] Corpus =
    [
        new() { Id = "b1", ISBN = "978-1", IPAddress = "10.0.0.2", Title = "One" },
        new() { Id = "b2", ISBN = "978-2", IPAddress = "10.0.0.1", Title = "Two" },
        new() { Id = "b3", ISBN = "978-3", IPAddress = "10.0.0.3", Title = "Three" },
    ];

    [Fact]
    public async Task Filters_and_sorts_on_acronym_members_address_the_stored_field()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var lease = Lease(NewPartition("acronym"));
        await Book.UpsertMany(Corpus);

        (await Book.Query(book => book.ISBN == "978-2")).Select(static book => book.Id).Should().Equal("b2");
        (await Book.Query(book => book.IPAddress == "10.0.0.3")).Select(static book => book.Id).Should().Equal("b3");
        (await Data<Book, string>.Query("{ \"ISBN\": { \"$in\": [\"978-1\", \"978-3\"] } }"))
            .Select(static book => book.Id).Order().Should().Equal("b1", "b3");

        (await Data<Book, string>.Page(1, 10, "IPAddress")).Select(static book => book.Id)
            .Should().Equal("b2", "b1", "b3");
        (await Data<Book, string>.Page(1, 10, "-ISBN")).Select(static book => book.Id)
            .Should().Equal("b3", "b2", "b1");
    }

    private sealed class Book : Entity<Book>
    {
        public string ISBN { get; set; } = "";
        public string IPAddress { get; set; } = "";
        public string Title { get; set; } = "";
    }
}
