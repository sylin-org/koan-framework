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
    public async Task Named_pipelines_and_inspection_form_one_source_first_journey()
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

        var inspector = source.Inspect();
        var containers = new List<StorageContainerDescriptor>();
        string? continuation = null;
        do
        {
            var page = await inspector.Containers(10, continuation);
            containers.AddRange(page.Containers);
            continuation = page.Continuation;
        } while (continuation is not null);

        var descriptor = containers.Should().ContainSingle(item => item.Address.Name == collectionName).Which;
        descriptor.ProviderKind.Should().Be("collection");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);

        var reference = await inspector.Resolve(StorageAddress.From(collectionName));
        var described = await inspector.Describe(reference);
        described.RecordShape.Should().BeNull("MongoDB collections do not promise one fixed document shape");

        var complete = await inspector.Sample(reference, 10);
        complete.Records.Should().HaveCount(2);
        complete.Fields.Select(field => field.Name).Should().Equal("_id", "title", "priority");
        complete.Completion.Should().Be(RecordSetCompletion.Complete);

        var bounded = await inspector.Sample(reference, 1);
        bounded.Records.Should().ContainSingle();
        bounded.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
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
