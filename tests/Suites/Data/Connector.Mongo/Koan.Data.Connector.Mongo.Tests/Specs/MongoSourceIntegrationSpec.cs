using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

public sealed class MongoSourceIntegrationSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Named_pipelines_execute_record_and_scalar_results()
    {
        RequireBackingStore();
        var collectionName = $"source_journey_{Guid.NewGuid():N}";
        var collection = new MongoClient(Fixture.ConnectionString)
            .GetDatabase(Fixture.Database)
            .GetCollection<BsonDocument>(collectionName);
        await collection.InsertManyAsync([
            new BsonDocument { ["_id"] = 1L, ["title"] = "polish", ["priority"] = 5 },
            new BsonDocument { ["_id"] = 2L, ["title"] = "ship", ["priority"] = 9 }
        ]);

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(Fixture.SettingsForBoot())
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source("Default").Query("work.ready", query => query
                    .Pipeline(collectionName,
                        """{ "$match": { "priority": { "$gte": "{{minimum}}" } } }""",
                        """{ "$project": { "_id": 0, "Id": "$_id", "Title": "$title" } }""")
                    .Parameter<int>("minimum"));
                koan.Data.Source("Default").Scalar<long>("work.count", query => query
                    .Pipeline(collectionName, """{ "$count": "total" }"""));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        var source = KoanData.Source("Default");
        var ready = await source.Query("work.ready", new { minimum = 7 });
        ready.Project<WorkItem>().Should().ContainSingle()
            .Which.Should().Be(new WorkItem(2, "ship"));
        (await source.Scalar<long>("work.count")).Should().Be(2);
    }

    [Fact]
    public void Registered_pipeline_rejects_write_stages_at_composition()
    {
        var compose = () => new MongoPipelineBinding(
            StorageAddress.From("work_items"),
            ["""{ "$out": "copied_items" }"""]);

        compose.Should().Throw<ArgumentException>()
            .WithMessage("*write stage '$out' is not allowed*");
    }

    private sealed record WorkItem(long Id, string Title);
}
