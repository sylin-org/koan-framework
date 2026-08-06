using Couchbase;
using Couchbase.Core.IO.Authentication.Authenticators;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Newtonsoft.Json.Linq;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Couchbase.Tests.Specs;

public sealed class CouchbaseSourceIntegrationSpec(CouchbaseFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<CouchbaseFixture>(fixture, output)
{
    [Fact]
    public async Task Named_reads_and_neutral_inspection_form_one_read_only_source_journey()
    {
        RequireBackingStore();
        const string sourceName = "SourceJourney";
        var scope = $"source_{Guid.NewGuid():N}";
        const string collection = "work_items";
        await Seed(scope, collection);
        var address = Qualified(scope, collection);

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(Settings(sourceName))
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                koan.Data.Source(sourceName).Query("work.ready", query => query
                    .Lane("Reports")
                    .Sql($"SELECT META(doc).id AS Id, doc.title AS Title FROM {address} AS doc WHERE doc.priority >= $minimum ORDER BY META(doc).id")
                    .Parameter<int>("minimum"));
                koan.Data.Source(sourceName).Scalar<long>("work.count", query => query
                    .Lane("Reports")
                    .Sql($"SELECT RAW COUNT(1) FROM {address} AS doc"));
                koan.Data.Source(sourceName).Query("work.mutate", query => query
                    .Lane("Reports")
                    .Sql($"UPDATE {address} AS doc SET doc.title = 'changed' WHERE META(doc).id = '2' RETURNING META(doc).id AS Id"));
            }))
            .StartAsync(TestContext.Current.CancellationToken);

        var source = KoanData.Source(sourceName);
        var ready = await source.Query("work.ready", new { minimum = 7 });
        ready.Project<WorkItem>().Should().ContainSingle()
            .Which.Should().Be(new WorkItem("2", "ship"));
        (await source.Scalar<long>("work.count")).Should().Be(2);
        await FluentActions.Invoking(() => source.Query("work.mutate"))
            .Should().ThrowAsync<CouchbaseException>();

        var inspector = source.Inspect();
        var containers = await AllContainers(inspector);
        var descriptor = containers.Should().ContainSingle(item =>
            item.Address.Namespace.SequenceEqual(new[] { scope }) && item.Address.Name == collection).Which;
        descriptor.ProviderKind.Should().Be("collection");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);
        descriptor.EffectiveOperations.Should().NotHaveFlag(StorageContainerOperations.Write);

        var reference = await inspector.Resolve(StorageAddress.From(scope, collection));
        var described = await inspector.Describe(reference);
        described.RecordShape.Should().BeNull("Couchbase collections do not promise one fixed document shape");

        var complete = await inspector.Sample(reference, 10);
        complete.Records.Should().HaveCount(2);
        complete.Fields.Select(field => field.Name).Should().Equal("title", "priority");
        complete.Completion.Should().Be(RecordSetCompletion.Complete);

        var bounded = await inspector.Sample(reference, 1);
        bounded.Records.Should().ContainSingle();
        bounded.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
    }

    private Dictionary<string, string?> Settings(string source) =>
        new(Fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            [$"Koan:Data:Sources:{source}:Adapter"] = "couchbase",
            [$"Koan:Data:Sources:{source}:ConnectionString"] = Fixture.ConnectionString,
            [$"Koan:Data:Sources:{source}:couchbase:Bucket"] = Fixture.Bucket,
            [$"Koan:Data:Sources:{source}:couchbase:Username"] = Fixture.AdminUser,
            [$"Koan:Data:Sources:{source}:couchbase:Password"] = Fixture.AdminPassword,
            [$"Koan:Data:Sources:{source}:StorageLifecycle"] = StorageLifecycle.External.ToString(),
            [$"Koan:Data:Sources:{source}:Access"] = DataSourceAccess.ReadOnly.ToString(),
            [$"Koan:Data:Sources:{source}:ReadLanes:Reports:ConnectionString"] = Fixture.ConnectionString
        };

    private async Task Seed(string scopeName, string collectionName)
    {
        using var cluster = await Connect();
        var bucket = await cluster.BucketAsync(Fixture.Bucket);
        try { await bucket.Collections.CreateScopeAsync(scopeName); }
        catch (ScopeExistsException) { }
        try { await bucket.Collections.CreateCollectionAsync(scopeName, collectionName, new CreateCollectionSettings()); }
        catch (CollectionExistsException) { }
        var scope = await bucket.ScopeAsync(scopeName);
        var collection = await scope.CollectionAsync(collectionName);
        await collection.UpsertAsync("1", new JObject { ["title"] = "polish", ["priority"] = 5 });
        await collection.UpsertAsync("2", new JObject { ["title"] = "ship", ["priority"] = 9 });
        await CreatePrimaryIndex(cluster, scopeName, collectionName);
    }

    private async Task<ICluster> Connect()
    {
        var options = new ClusterOptions { ConnectionString = Fixture.ConnectionString }
            .WithAuthenticator(new PasswordAuthenticator(Fixture.AdminUser, Fixture.AdminPassword));
        return await Cluster.ConnectAsync(options);
    }

    private async Task CreatePrimaryIndex(ICluster cluster, string scope, string collection)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await cluster.QueryAsync<dynamic>(
                    $"CREATE PRIMARY INDEX IF NOT EXISTS ON {Qualified(scope, collection)} USING GSI",
                    new QueryOptions().Readonly(false).Timeout(TimeSpan.FromSeconds(10)));
                await foreach (var _ in result.Rows) { }
                return;
            }
            catch (ServiceNotAvailableException) when (attempt < 59)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private string Qualified(string scope, string collection) =>
        $"`{Fixture.Bucket}`.`{scope}`.`{collection}`";

    private static async Task<List<StorageContainerDescriptor>> AllContainers(IDataSourceInspector inspector)
    {
        var containers = new List<StorageContainerDescriptor>();
        string? continuation = null;
        do
        {
            var page = await inspector.Containers(25, continuation);
            containers.AddRange(page.Containers);
            continuation = page.Continuation;
        } while (continuation is not null);
        return containers;
    }

    private sealed record WorkItem(string Id, string Title);
}
