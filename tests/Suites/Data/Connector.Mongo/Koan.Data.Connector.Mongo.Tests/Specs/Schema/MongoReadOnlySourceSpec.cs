using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Sources;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Schema;

/// <summary>
/// ReadOnly forbids storage-lifecycle mutation, so a Managed + ReadOnly source never creates a collection or an index,
/// even on its first read (NK-030, NK-131).
/// </summary>
public sealed class MongoReadOnlySourceSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task A_missing_collection_is_refused_and_not_created()
    {
        RequireBackingStore();
        var database = $"koan_ro_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(Settings(database));
        try
        {
            using (EntityContext.Source("Reporting"))
                await FluentActions.Invoking(() => Report.Get("missing"))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ReadOnly*will not create*");

            var names = await (await Client().GetDatabase(database).ListCollectionNamesAsync()).ToListAsync();
            names.Should().BeEmpty("a ReadOnly source must not create the collection");
        }
        finally
        {
            await Client().DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task An_existing_collection_is_read_without_creating_indexes()
    {
        RequireBackingStore();
        var database = $"koan_ro_{Guid.CreateVersion7():N}";
        await using var host = await BootAsync(Settings(database));
        try
        {
            string name;
            using (EntityContext.Source("Reporting"))
                name = Koan.Data.Core.Configuration.AdapterNaming.GetOrCompute<Report, string>(host.Services);
            var collection = Client().GetDatabase(database).GetCollection<BsonDocument>(name);
            await collection.InsertOneAsync(new BsonDocument { ["_id"] = "r1", ["code"] = "A" });

            using (EntityContext.Source("Reporting"))
                (await Report.Get("r1"))!.Code.Should().Be("A");

            using var cursor = await collection.Indexes.ListAsync();
            (await cursor.ToListAsync()).Select(static index => index["name"].AsString)
                .Should().Equal("_id_");
        }
        finally
        {
            await Client().DropDatabaseAsync(database);
        }
    }

    private MongoClient Client() => new(Fixture.ConnectionString);

    private Dictionary<string, string?> Settings(string database) => new(StringComparer.Ordinal)
    {
        ["Koan:Data:Sources:Reporting:Adapter"] = "mongo",
        ["Koan:Data:Sources:Reporting:ConnectionString"] = Fixture.ConnectionString,
        ["Koan:Data:Sources:Reporting:Database"] = database,
        ["Koan:Data:Sources:Reporting:Access"] = DataSourceAccess.ReadOnly.ToString(),
    };

    private sealed class Report : Entity<Report>
    {
        [Index] public string Code { get; set; } = "";
    }
}
