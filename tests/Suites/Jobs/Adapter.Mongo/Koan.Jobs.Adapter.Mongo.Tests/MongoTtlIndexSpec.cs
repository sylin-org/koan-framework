using AwesomeAssertions;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Koan.Jobs.Adapter.Mongo.Tests;

/// <summary>
/// JOBS-0005 §20.4: `[Index(Ttl = true)]` on `JobRecord.ExpireAt` must materialize as a real Mongo TTL index
/// (`expireAfterSeconds`). The actual row deletion is Mongo's background TTL monitor (~60s) — out of scope for a fast
/// test; this proves the wiring (the index exists) through real `AddKoan` schema-ensure.
/// </summary>
public sealed class MongoTtlIndexSpec : IClassFixture<MongoJobsFixture>
{
    private readonly MongoJobsFixture _fx;
    public MongoTtlIndexSpec(MongoJobsFixture fx) => _fx = fx;

    [Fact]
    public async Task index_ttl_materializes_a_mongo_ttl_index()
    {
        await using var h = await JobsHarness.StartWithSettingsAsync(_fx.Settings);
        await JobRecord.Query(r => r.Status == JobStatus.Queued);   // touch the collection so EnsureIndexes runs

        var client = new MongoClient(_fx.Settings["Koan:Data:Mongo:ConnectionString"]);
        var db = client.GetDatabase(_fx.Settings["Koan:Data:Mongo:Database"]);

        var ttlFound = false;
        foreach (var name in await (await db.ListCollectionNamesAsync()).ToListAsync())
        {
            var indexes = await (await db.GetCollection<BsonDocument>(name).Indexes.ListAsync()).ToListAsync();
            if (indexes.Any(ix => ix.Contains("expireAfterSeconds"))) { ttlFound = true; break; }
        }

        ttlFound.Should().BeTrue("the [Index(Ttl=true)] on JobRecord.ExpireAt should create a Mongo TTL index");
    }
}
