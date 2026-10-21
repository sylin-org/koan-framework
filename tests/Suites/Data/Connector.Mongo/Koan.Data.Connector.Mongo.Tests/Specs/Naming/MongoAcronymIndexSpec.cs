using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Naming;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Naming;

/// <summary>
/// Index keys use the field names the serializer writes (ISBN is stored as "isbn"), so a unique [Index] on an acronym
/// member enforces uniqueness, an existing collection gains the correctly keyed index, and an explicitly named index
/// that still has the legacy keys is refused with the way to drop it (NK-029, DATA-0145).
/// </summary>
public sealed class MongoAcronymIndexSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task A_unique_index_on_an_acronym_member_is_keyed_by_the_stored_field_and_enforces_uniqueness()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("acronym-unique");
        using var lease = Lease(partition);

        await new Volume { Id = "v1", ISBN = "978-1" }.Save();
        await new Volume { Id = "v2", ISBN = "978-2" }.Save();
        await FluentActions.Invoking(() => new Volume { Id = "v3", ISBN = "978-1" }.Save())
            .Should().ThrowAsync<MongoWriteException>("a second row with a held ISBN violates the unique index");

        var keys = (await Indexes(host.Services, partition, typeof(Volume))).Where(static index => index["name"].AsString != "_id_").ToArray();
        keys.Should().ContainSingle();
        keys[0]["key"].AsBsonDocument.Names.Should().Equal("isbn");
        keys[0]["unique"].ToBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_existing_collection_gains_the_correctly_keyed_index_beside_the_legacy_one()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("acronym-existing");
        using var lease = Lease(partition);
        var collection = Collection(host.Services, partition, typeof(Shelf));
        await collection.InsertOneAsync(new BsonDocument { ["_id"] = "s1", ["isbn"] = "978-1" });
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("iSBN"), new CreateIndexOptions { Name = "iSBN_1" }));

        (await Shelf.Get("s1"))!.ISBN.Should().Be("978-1");

        var names = (await Indexes(host.Services, partition, typeof(Shelf))).Select(static index => index["name"].AsString).ToArray();
        names.Should().BeEquivalentTo("_id_", "iSBN_1", "isbn_1");
    }

    [Fact]
    public async Task An_explicitly_named_index_with_the_legacy_keys_is_refused_with_the_way_to_drop_it()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("acronym-named");
        using var lease = Lease(partition);
        var collection = Collection(host.Services, partition, typeof(Catalogue));
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("iSBN"), new CreateIndexOptions { Name = "ux_isbn", Unique = true }));

        await FluentActions.Invoking(() => Catalogue.Get("missing"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'ux_isbn'*iSBN*isbn*drop*");

        await collection.Indexes.DropOneAsync("ux_isbn");
        (await Catalogue.Get("missing")).Should().BeNull();
        var created = (await Indexes(host.Services, partition, typeof(Catalogue))).Single(static index => index["name"].AsString == "ux_isbn");
        created["key"].AsBsonDocument.Names.Should().Equal("isbn");
    }

    private IMongoCollection<BsonDocument> Collection(IServiceProvider services, string partition, Type entity)
        => new MongoClient(Fixture.ConnectionString).GetDatabase(Fixture.Database)
            .GetCollection<BsonDocument>(StorageNameGenerator.Generate(entity, partition,
                new MongoAdapterFactory().GetNamingCapability(services)));

    private async Task<List<BsonDocument>> Indexes(IServiceProvider services, string partition, Type entity)
    {
        using var cursor = await Collection(services, partition, entity).Indexes.ListAsync();
        return await cursor.ToListAsync();
    }

    private sealed class Volume : Entity<Volume>
    {
        [Index(Unique = true)] public string ISBN { get; set; } = "";
    }

    private sealed class Shelf : Entity<Shelf>
    {
        [Index] public string ISBN { get; set; } = "";
    }

    private sealed class Catalogue : Entity<Catalogue>
    {
        [Index(Name = "ux_isbn", Unique = true)] public string ISBN { get; set; } = "";
    }
}
