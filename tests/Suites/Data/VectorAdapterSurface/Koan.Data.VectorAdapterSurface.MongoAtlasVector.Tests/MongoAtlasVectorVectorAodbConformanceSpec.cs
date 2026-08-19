using System.Diagnostics;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Connector.MongoAtlasVector;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.MongoAtlasVector.Tests;

/// <summary>Live conformance suite for native exact search on MongoDB Atlas.</summary>
public sealed class MongoAtlasVectorVectorAodbConformanceSpec(MongoAtlasVectorTestFactory fixture)
    : VectorAodbConformanceSpecsBase
{
    private const string SearchIndex = "koan_vector";
    private const string EmbeddingField = "__koan_embedding";

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        if (!fixture.IsAvailable) return (null, fixture.UnavailableReason);
        await fixture.Reset().ConfigureAwait(false);
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Mongo:DisableAutoDetection"] = "true",
            ["Koan:Data:Mongo:ConnectionString"] = fixture.ConnectionString,
            ["Koan:Data:Mongo:Database"] = fixture.RecordDatabase,
            ["Koan:Data:MongoAtlasVector:DisableAutoDetection"] = "true",
            ["Koan:Data:MongoAtlasVector:ConnectionString"] = fixture.ConnectionString,
            ["Koan:Data:MongoAtlasVector:Database"] = fixture.VectorDatabase,
            ["Koan:Data:Sources:Default:Adapter"] = "mongo",
            ["Koan:Data:Sources:VectorConformance:Adapter"] = "mongo-atlas-vector",
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "mongo-atlas-vector",
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "mongo-atlas-vector",
            ["Koan:Data:Sources:VectorReadOnly:Adapter"] = "mongo-atlas-vector",
            ["Koan:Data:Sources:VectorReadOnly:Access"] = "ReadOnly",
            ["Koan:Data:Sources:VectorExternal:Adapter"] = "mongo-atlas-vector",
            ["Koan:Data:Sources:VectorExternal:StorageLifecycle"] = "External"
        };
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                var shared = koan.Data.Source("VectorConformance");
                shared.Vector<VectorConformanceTenantDoc>(space => Space(space, "tenant"));
                shared.Vector<VectorConformancePartitionDoc>(space => Space(space, "partition"));
                shared.Vector<MongoAtlasVectorEuclideanDoc>(space =>
                    Space(space, "euclidean", VectorMetric.Euclidean));
                shared.Vector<MongoAtlasVectorDotProductDoc>(space =>
                    Space(space, "dot", VectorMetric.DotProduct));
                koan.Data.Source("Default").Vector<MongoAtlasVectorCoexistDoc>(space => Space(space, "coexist"));
                koan.Data.Source("VectorReadOnly").Vector<MongoAtlasVectorReadOnlyDoc>(space =>
                    Space(space, "readonly"));
                koan.Data.Source("VectorExternal").Vector<MongoAtlasVectorExternalDoc>(space =>
                    Space(space, "external"));
                koan.Data.Source(SourceA).Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
                koan.Data.Source(SourceB).Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
            }))
            .StartAsync()
            .ConfigureAwait(false);
        return (host, null);
    }

    protected override Task ProveVectorAnnexCellAsync(string acceptanceId, string proof)
    {
        if (!fixture.IsAvailable) return base.ProveVectorAnnexCellAsync(acceptanceId, proof);
        return acceptanceId switch
        {
            "V-01" => SpacePlan(),
            "V-02" => EmbeddingBoundaries(),
            "V-03" => Upsert(),
            "V-04" => Delete(),
            "V-05" => GetMany(),
            "V-06" => Metadata(),
            "V-07" => SearchOrder(),
            "V-08" => Similarity(),
            "V-09" => SpaceIntegrity(),
            "V-10" => ExecutionTruth(),
            "V-11" => SessionVisibility(),
            "V-12" => EventualDecline(),
            "V-13" => NativeFilter(),
            "V-14" => HybridDecline(),
            "V-15" => NamedSpaceDecline(),
            "V-16" => ContinuationDecline(),
            "V-17" => Bulk(),
            "V-18" => AtomicDecline(),
            "V-19" => ExportDecline(),
            "V-20" => LifecyclePolicy(),
            "V-21" => Isolation(),
            "V-22" => Coordination(),
            "V-23" => FailureLifecycle(),
            "V-24" => WarmPath(),
            _ => base.ProveVectorAnnexCellAsync(acceptanceId, proof)
        };
    }

    private static float[] Point(float x, float y = 0f) => [x, y, 0f, 0f, 0f, 0f, 0f, 0f];

    private IVectorSearchRepository<VectorConformancePartitionDoc, string> Repository() =>
        Host!.Services.GetRequiredService<IVectorService>()
            .TryGetRepository<VectorConformancePartitionDoc, string>()
        ?? throw new InvalidOperationException("Mongo Atlas Vector repository did not resolve.");

    private static object? Value(DataObject value, string name) =>
        Assert.Single(value.Properties, property => property.Name == name).Value;

    private string Collection<TEntity>(string source = "VectorConformance", string? partition = null)
        where TEntity : class
    {
        var factory = Host!.Services.GetRequiredService<MongoAtlasVectorAdapterFactory>();
        return StorageNameGenerator.Resolve(
            factory.Provider,
            typeof(TEntity),
            partition,
            source,
            () => factory.GetNamingCapability(Host.Services),
            Host.Services);
    }

    private async Task SpacePlan()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("bad-plan", [1f, 0f]));
        Assert.Contains("requires 8", error.Message, StringComparison.Ordinal);
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("bad-plan"));

        var factory = Host!.Services.GetRequiredService<MongoAtlasVectorAdapterFactory>();
        var dimensionCollection = Collection<MongoAtlasVectorWrongDimensionDoc>("Default");
        await fixture.CreateWrongShapeIndex(dimensionCollection, SearchIndex, 4, "cosine");
        var dimensionRepository = factory.Create<MongoAtlasVectorWrongDimensionDoc, string>(
            Host.Services,
            new VectorSpacePlan("Default", "wrong-dimension", 8, VectorMetric.Cosine, VectorVisibility.Session));
        var dimension = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dimensionRepository.VectorEnsureCreated());
        Assert.Contains("dimension", dimension.Message, StringComparison.OrdinalIgnoreCase);

        var metricCollection = Collection<MongoAtlasVectorWrongMetricDoc>("Default");
        await fixture.CreateWrongShapeIndex(metricCollection, SearchIndex, 8, "euclidean");
        var metricRepository = factory.Create<MongoAtlasVectorWrongMetricDoc, string>(
            Host.Services,
            new VectorSpacePlan("Default", "wrong-metric", 8, VectorMetric.Cosine, VectorVisibility.Session));
        var metric = await Assert.ThrowsAsync<InvalidOperationException>(
            () => metricRepository.VectorEnsureCreated());
        Assert.True(
            metric.Message.Contains("metric", StringComparison.OrdinalIgnoreCase) ||
            metric.Message.Contains("similarity", StringComparison.OrdinalIgnoreCase),
            $"Expected a native metric mismatch, but received: {metric.Message}");
    }

    private async Task EmbeddingBoundaries()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("empty", []));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("short", [1f]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("nan", Point(float.NaN)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("infinite", Point(float.PositiveInfinity)));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("zero", Point(0)));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("nan"));

        await Vector<MongoAtlasVectorEuclideanDoc>.Save("euclidean-zero", Point(0));
        var zero = await Vector<MongoAtlasVectorEuclideanDoc>.Search(Point(0), query => query.Top(1));
        Assert.Equal("euclidean-zero", Assert.Single(zero.Items).Id);
    }

    private async Task Upsert()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(2), new { Version = 1 });
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 3), new { Version = 2 });
        var point = Assert.IsType<VectorPoint<string>>(await Vector<VectorConformancePartitionDoc>.Get("same"));
        Assert.Equal(Point(0, 3), point.Embedding.ToArray(), new FloatToleranceComparer(0.00001f));
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(point.Metadata), "Version")));
        Assert.Single((await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1), query => query.Top(10))).Items);

        await new MongoAtlasVectorCoexistDoc { Id = "coexist", Title = "record payload" }.Save();
        await Vector<MongoAtlasVectorCoexistDoc>.Save("coexist", Point(1), new { Title = "vector payload" });
        Assert.Equal("record payload", (await MongoAtlasVectorCoexistDoc.Get("coexist"))?.Title);
        Assert.Equal(
            "vector payload",
            Value(Assert.IsType<DataObject>((await Vector<MongoAtlasVectorCoexistDoc>.Get("coexist"))?.Metadata),
                "Title"));

        var collection = Collection<MongoAtlasVectorCoexistDoc>("Default");
        var vector = await fixture.Database().GetCollection<BsonDocument>(collection)
            .Find(Builders<BsonDocument>.Filter.Eq("__koan_id", "coexist"))
            .SingleAsync();
        Assert.Equal("coexist", vector["__koan_id"].AsString);
        var recordCollections = await (await fixture.Database(fixture.RecordDatabase)
                .ListCollectionNamesAsync())
            .ToListAsync();
        Assert.NotEmpty(recordCollections);
    }

    private async Task Delete()
    {
        await Vector<VectorConformancePartitionDoc>.Save("delete", Point(1));
        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("delete"));
        Assert.False(await Vector<VectorConformancePartitionDoc>.Delete("delete"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Vector<VectorConformancePartitionDoc>.Delete("delete", canceled.Token));
    }

    private async Task GetMany()
    {
        await Vector<VectorConformancePartitionDoc>.Save("one", Point(1));
        var points = await Vector<VectorConformancePartitionDoc>.Get(["one", "missing", "one"]);
        Assert.Equal(3, points.Count);
        Assert.Equal("one", points[0]!.Id);
        Assert.Null(points[1]);
        Assert.Equal("one", points[2]!.Id);
    }

    private async Task Metadata()
    {
        var bytes = new byte[] { 1, 2, 3 };
        await Vector<VectorConformancePartitionDoc>.Save("meta", Point(1), new
        {
            Title = "neutral",
            Detail = new { Count = 2 },
            Tags = new[] { "a", "b" },
            Blob = bytes
        });
        bytes[0] = 9;
        var metadata = Assert.IsType<DataObject>((await Vector<VectorConformancePartitionDoc>.Get("meta"))!.Metadata);
        Assert.Equal("neutral", Value(metadata, "Title"));
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(Value(metadata, "Detail")), "Count")));
        Assert.Equal(2, Assert.IsType<DataArray>(Value(metadata, "Tags")).Items.Count);
        Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(Value(metadata, "Blob")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("reserved", Point(1),
                new Dictionary<string, object?> { ["__koan_user"] = "collision" }));
    }

    private async Task SearchOrder()
    {
        foreach (var id in new[] { "c", "a", "b" })
            await Vector<VectorConformancePartitionDoc>.Save(id, Point(1));
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(2));
        Assert.Equal(["a", "b"], result.Items.Select(item => item.Id));
        Assert.All(result.Items, item => Assert.InRange(item.Similarity, 0d, 1d));
    }

    private async Task Similarity()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        await Vector<VectorConformancePartitionDoc>.Save("orthogonal", Point(0, 1));
        await Vector<VectorConformancePartitionDoc>.Save("opposite", Point(-1));
        var cosine = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(3));
        Assert.Equal(["same", "orthogonal", "opposite"], cosine.Items.Select(item => item.Id));
        Assert.True(cosine.Items[0].Similarity > cosine.Items[1].Similarity);
        Assert.True(cosine.Items[1].Similarity > cosine.Items[2].Similarity);
        Assert.Equal(VectorMetric.Cosine, cosine.Execution.Metric);
        Assert.Equal(VectorSearchAccuracy.Exact, cosine.Execution.Accuracy);

        await Vector<MongoAtlasVectorEuclideanDoc>.Save("same", Point(0));
        await Vector<MongoAtlasVectorEuclideanDoc>.Save("five-away", Point(3, 4));
        var euclidean = await Vector<MongoAtlasVectorEuclideanDoc>.Search(Point(0), query => query.Top(2));
        Assert.Equal(["same", "five-away"], euclidean.Items.Select(item => item.Id));
        Assert.Equal(1d, euclidean.Items[0].Similarity, 10);
        Assert.Equal(1d / 6d, euclidean.Items[1].Similarity, 5);
        Assert.Equal(VectorMetric.Euclidean, euclidean.Execution.Metric);
        Assert.Equal(VectorSearchAccuracy.Exact, euclidean.Execution.Accuracy);

        await Vector<MongoAtlasVectorDotProductDoc>.Save("positive", Point(2));
        await Vector<MongoAtlasVectorDotProductDoc>.Save("zero", Point(0, 1));
        await Vector<MongoAtlasVectorDotProductDoc>.Save("negative", Point(-1));
        var dot = await Vector<MongoAtlasVectorDotProductDoc>.Search(Point(1), query => query.Top(3));
        Assert.Equal(["positive", "zero", "negative"], dot.Items.Select(item => item.Id));
        Assert.True(dot.Items[0].Similarity > dot.Items[1].Similarity);
        Assert.True(dot.Items[1].Similarity > dot.Items[2].Similarity);
        Assert.Equal(VectorMetric.DotProduct, dot.Execution.Metric);
        Assert.Equal(VectorSearchAccuracy.Exact, dot.Execution.Accuracy);

        Assert.All(cosine.Items.Concat(euclidean.Items).Concat(dot.Items), item =>
        {
            Assert.True(double.IsFinite(item.Similarity));
            Assert.InRange(item.Similarity, 0d, 1d);
        });
    }

    private async Task SpaceIntegrity()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Space("other")));
        Assert.Contains("Available space: partition", error.Message, StringComparison.Ordinal);
    }

    private async Task ExecutionTruth()
    {
        await Vector<VectorConformancePartitionDoc>.Save("one", Point(1));
        var collection = Collection<VectorConformancePartitionDoc>();
        var index = await fixture.WaitForSearchIndex(collection, SearchIndex);
        Assert.Equal("search", index["type"].AsString);
        var vector = index["latestDefinition"]["mappings"]["fields"][EmbeddingField].AsBsonDocument;
        Assert.Equal(8, vector["numDimensions"].AsInt32);
        Assert.Equal("cosine", vector["similarity"].AsString);

        await fixture.EnableProfiling(true);
        VectorSearchResult<string> result;
        try
        {
            result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));
        }
        finally
        {
            await fixture.EnableProfiling(false);
        }

        Assert.Equal(VectorSearchAccuracy.Exact, result.Execution.Accuracy);
        Assert.Equal(VectorMetric.Cosine, result.Execution.Metric);
        Assert.Null(result.Execution.CandidatesConsidered);
        Assert.Null(result.Continuation);
        var command = Assert.IsType<BsonDocument>(await fixture.LastVectorSearchCommand(collection));
        var native = NativeVectorSearch(command);
        Assert.True(native["exact"].AsBoolean);
        Assert.Equal(EmbeddingField, native["path"].AsString);
        Assert.False(native.Contains("numCandidates"));
    }

    private async Task SessionVisibility()
    {
        await Vector<VectorConformancePartitionDoc>.Save("visible", Point(1), new { Revision = 1 });
        await Vector<VectorConformancePartitionDoc>.Save("visible", Point(0, 1), new { Revision = 2 });

        var current = Assert.IsType<VectorPoint<string>>(await Vector<VectorConformancePartitionDoc>.Get("visible"));
        Assert.Equal(Point(0, 1), current.Embedding.ToArray(), new FloatToleranceComparer(0.00001f));
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(current.Metadata), "Revision")));
        var search = await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1), query => query.Top(10));
        var visible = Assert.Single(search.Items, item => item.Id == "visible");
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(visible.Metadata), "Revision")));

        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("visible"));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("visible"));
        Assert.DoesNotContain(
            (await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1), query => query.Top(10))).Items,
            item => item.Id == "visible");
    }

    private Task EventualDecline()
    {
        var factory = Host!.Services.GetRequiredService<MongoAtlasVectorAdapterFactory>();
        var plan = new VectorSpacePlan("eventual", "events", 8, VectorMetric.Cosine, VectorVisibility.Eventual);
        Assert.Throws<NotSupportedException>(() =>
            factory.Create<VectorConformancePartitionDoc, string>(Host.Services, plan));
        return Task.CompletedTask;
    }

    private async Task NativeFilter()
    {
        var corpus = new (string Id, Dictionary<string, object?> Metadata)[]
        {
            ("a", new() { ["Category"] = "legal", ["Priority"] = 1L, ["Tags"] = new[] { "x", "y" } }),
            ("b", new() { ["Category"] = "legal", ["Priority"] = 3L, ["Tags"] = new[] { "y", "z" } }),
            ("c", new() { ["Category"] = "finance", ["Priority"] = 2L, ["Tags"] = new[] { "x" } }),
            ("d", new() { ["Category"] = "finance", ["Priority"] = 5L }),
            ("e", new() { ["Priority"] = 4L, ["Tags"] = new[] { "z" } })
        };
        foreach (var item in corpus)
            await Vector<VectorConformancePartitionDoc>.Save(item.Id, Point(1), item.Metadata);

        static Filter Leaf(string field, FilterOperator operation, object? value) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Of(value));
        static Filter Set(string field, FilterOperator operation, params object?[] values) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Many(values));
        var cases = new Filter[]
        {
            Filter.Eq("Category", "legal"),
            Leaf("Category", FilterOperator.Ne, "legal"),
            Leaf("Priority", FilterOperator.Gt, 3L),
            Leaf("Priority", FilterOperator.Gte, 3L),
            Leaf("Priority", FilterOperator.Lt, 3L),
            Leaf("Priority", FilterOperator.Lte, 3L),
            Filter.In("Category", new object[] { "legal", "finance" }),
            Set("Category", FilterOperator.Nin, "legal"),
            Leaf("Tags", FilterOperator.Has, "x"),
            Set("Tags", FilterOperator.HasAny, "z", "x"),
            Set("Tags", FilterOperator.HasAll, "x", "y"),
            Set("Tags", FilterOperator.HasNone, "x"),
            Leaf("Tags", FilterOperator.Size, 2),
            Leaf("Category", FilterOperator.Exists, true),
            Leaf("Category", FilterOperator.Exists, false),
            Filter.All(Filter.Eq("Category", "legal"), Leaf("Priority", FilterOperator.Gte, 3L)),
            Filter.Any(Filter.Eq("Category", "finance"), Leaf("Priority", FilterOperator.Lte, 1L)),
            Filter.Negate(Filter.Eq("Category", "legal"))
        };
        foreach (var filter in cases)
        {
            var expected = corpus
                .Where(item => DictionaryFilterEvaluator.Compile(filter)(item.Metadata))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var result = await Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(100).Where(filter));
            var actual = result.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            Assert.True(expected.SetEquals(actual),
                $"Filter {filter} expected [{string.Join(',', expected)}] but returned [{string.Join(',', actual)}].");
        }

        await Vector<VectorConformancePartitionDoc>.Save("prefilter-near", Point(1), new { Gate = "excluded" });
        await Vector<VectorConformancePartitionDoc>.Save("prefilter-far", Point(0, 1), new { Gate = "included" });
        var collection = Collection<VectorConformancePartitionDoc>();
        await fixture.EnableProfiling(true);
        VectorSearchResult<string> prefiltered;
        try
        {
            prefiltered = await Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(1).Where(Filter.Eq("Gate", "included")));
        }
        finally
        {
            await fixture.EnableProfiling(false);
        }
        Assert.Equal("prefilter-far", Assert.Single(prefiltered.Items).Id);
        Assert.True(NativeVectorSearch(Assert.IsType<BsonDocument>(
            await fixture.LastVectorSearchCommand(collection))).Contains("filter"));

        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Category", FilterOperator.StartsWith, "fin"))));
        Assert.True(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.Filters));
    }

    private async Task HybridDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.Hybrid));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Text("words").SemanticWeight(.5)));
    }

    private async Task NamedSpaceDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.MultiVectorPerEntity));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Space("undeclared")));
    }

    private async Task ContinuationDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.NativeContinuation));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.After("not-a-snapshot")));
    }

    private async Task Bulk()
    {
        var capabilities = Vector<VectorConformancePartitionDoc>.GetCapabilities();
        Assert.True(capabilities.Has(VectorCaps.BulkUpsert));
        Assert.True(capabilities.Has(VectorCaps.BulkDelete));
        await Vector<VectorConformancePartitionDoc>.Save("a", Point(1));
        var saved = await Repository().Save([
            new VectorPoint<string>("a", Point(0, 1), null),
            new VectorPoint<string>("b", Point(1), null)
        ], VectorScope.Unscoped);
        Assert.Equal([MutationOutcome.Updated, MutationOutcome.Inserted], saved.Items.Select(item => item.Outcome));
        Assert.Equal(BatchAtomicity.NotGuaranteed, saved.Atomicity);
        var deleted = await Repository().Delete(["b", "missing", "a"], VectorScope.Unscoped);
        Assert.Equal([MutationOutcome.Deleted, MutationOutcome.Missing, MutationOutcome.Deleted],
            deleted.Items.Select(item => item.Outcome));
    }

    private async Task AtomicDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.AtomicBatch));
        var repository = Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => repository.Save([
            new VectorPoint<string>("valid", Point(1), null),
            new VectorPoint<string>("invalid", new float[] { 1f }, null)
        ], VectorScope.Unscoped));
        Assert.Null(await repository.Get("valid", VectorScope.Unscoped));
        var result = await repository.Save(
            [new VectorPoint<string>("accepted", Point(1), null)],
            VectorScope.Unscoped);
        Assert.Equal(BatchAtomicity.NotGuaranteed, result.Atomicity);
    }

    private Task ExportDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.StreamingResults));
        Assert.Throws<NotSupportedException>(() => Repository().ExportAll());
        return Task.CompletedTask;
    }

    private async Task LifecyclePolicy()
    {
        await Vector<VectorConformancePartitionDoc>.EnsureCreated();
        await Vector<VectorConformancePartitionDoc>.Sync();
        await Vector<VectorConformancePartitionDoc>.Save("clear", Point(1));
        await Vector<VectorConformancePartitionDoc>.Clear();
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("clear"));

        using (EntityContext.Source("VectorReadOnly"))
        {
            await Vector<MongoAtlasVectorReadOnlyDoc>.Sync();
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<MongoAtlasVectorReadOnlyDoc>.Save("blocked", Point(1)));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<MongoAtlasVectorReadOnlyDoc>.Clear());
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<MongoAtlasVectorReadOnlyDoc>.EnsureCreated());
        }
        using (EntityContext.Source("VectorExternal"))
        {
            Assert.Null(await Vector<MongoAtlasVectorExternalDoc>.Get("missing"));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<MongoAtlasVectorExternalDoc>.EnsureCreated());
        }
    }

    private async Task Isolation()
    {
        using (EntityContext.Partition("alpha"))
            await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        using (EntityContext.Partition("beta"))
            await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1));
        using (EntityContext.Partition("alpha"))
        {
            Assert.Equal(Point(1), (await Vector<VectorConformancePartitionDoc>.Get("same"))!.Embedding.ToArray(),
                new FloatToleranceComparer(0.00001f));
            Assert.Equal("same", Assert.Single((await Vector<VectorConformancePartitionDoc>
                .Search(Point(1), query => query.Top(10))).Items).Id);
            Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("same"));
        }
        using (EntityContext.Partition("beta"))
            Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("same"));

        using (EntityContext.Partition("alpha"))
            await Vector<VectorConformancePartitionDoc>.Save("clear", Point(1));
        using (EntityContext.Partition("beta"))
            await Vector<VectorConformancePartitionDoc>.Save("clear", Point(0, 1));
        using (EntityContext.Partition("alpha"))
            await Vector<VectorConformancePartitionDoc>.Clear();
        using (EntityContext.Partition("beta"))
            Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("clear"));

        using (EntityContext.Source(SourceA))
            await Vector<VectorConformanceShardedDoc>.Save("same", Point(1));
        using (EntityContext.Source(SourceB))
            await Vector<VectorConformanceShardedDoc>.Save("same", Point(0, 1));
        using (EntityContext.Source(SourceA))
        {
            Assert.Equal(Point(1), (await Vector<VectorConformanceShardedDoc>.Get("same"))!.Embedding.ToArray(),
                new FloatToleranceComparer(0.00001f));
            Assert.Equal("same", Assert.Single((await Vector<VectorConformanceShardedDoc>
                .Search(Point(1), query => query.Top(10))).Items).Id);
            Assert.True(await Vector<VectorConformanceShardedDoc>.Delete("same"));
        }
        using (EntityContext.Source(SourceB))
            Assert.NotNull(await Vector<VectorConformanceShardedDoc>.Get("same"));

        using (EntityContext.Source(SourceA))
            await Vector<VectorConformanceShardedDoc>.Save("clear", Point(1));
        using (EntityContext.Source(SourceB))
            await Vector<VectorConformanceShardedDoc>.Save("clear", Point(0, 1));
        using (EntityContext.Source(SourceA))
            await Vector<VectorConformanceShardedDoc>.Clear();
        using (EntityContext.Source(SourceB))
            Assert.NotNull(await Vector<VectorConformanceShardedDoc>.Get("clear"));
    }

    private async Task Coordination()
    {
        using (EntityContext.Transaction("vector-annex"))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Vector<VectorConformancePartitionDoc>.SaveWithVector(
                    new VectorConformancePartitionDoc { Id = "coordinated" }, Point(1)));
            Assert.Contains("does not claim cross-store transaction atomicity", error.Message, StringComparison.Ordinal);
        }
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("coordinated"));
    }

    private async Task FailureLifecycle()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("canceled", Point(1), ct: canceled.Token));

        await Vector<VectorConformancePartitionDoc>.Save("durable", Point(1));
        var collection = Collection<VectorConformancePartitionDoc>();
        await fixture.Restart();
        _ = await fixture.WaitForSearchIndex(collection, SearchIndex);
        Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("durable"));
        Assert.Contains(
            (await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(10))).Items,
            item => item.Id == "durable");

        var factory = Host!.Services.GetRequiredService<MongoAtlasVectorAdapterFactory>();
        var repository = factory.Create<VectorConformancePartitionDoc, string>(Host.Services,
            new VectorSpacePlan("disposed", "disposed", 8, VectorMetric.Cosine, VectorVisibility.Session));
        await ((IAsyncDisposable)repository).DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            repository.Save(new VectorPoint<string>("x", Point(1), null), VectorScope.Unscoped));
    }

    private async Task WarmPath()
    {
        await Vector<VectorConformancePartitionDoc>.Save("warm", Point(1));
        _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
        _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 16; index++)
        {
            await Vector<VectorConformancePartitionDoc>.Save($"warm-{index}", Point(1));
            _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
            _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(4));
        }
        timer.Stop();
        var delta = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        Assert.True(delta < 64 * 1024 * 1024, $"16 Atlas warm cycles allocated {delta:N0} bytes.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(25), $"16 Atlas warm cycles took {timer.Elapsed}.");
    }

    private static BsonDocument NativeVectorSearch(BsonDocument profile) =>
        profile["command"].AsBsonDocument["pipeline"].AsBsonArray[0].AsBsonDocument["$search"]
            .AsBsonDocument["vectorSearch"].AsBsonDocument;

    private static void Space<TEntity>(
        VectorSpaceBuilder<TEntity> space,
        string name,
        VectorMetric metric = VectorMetric.Cosine)
        where TEntity : class, IEntity<string> => space
        .Name(name)
        .Dimensions(8)
        .Metric(metric)
        .Visibility(VectorVisibility.Session);

    private sealed class FloatToleranceComparer(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float left, float right) => Math.Abs(left - right) <= tolerance;
        public int GetHashCode(float value) => 0;
    }
}

[HostScoped]
public sealed class MongoAtlasVectorReadOnlyDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorReadOnlyDoc>;

[HostScoped]
public sealed class MongoAtlasVectorExternalDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorExternalDoc>;

public sealed class MongoAtlasVectorWrongDimensionDoc :
    Koan.Data.Core.Model.Entity<MongoAtlasVectorWrongDimensionDoc>;

public sealed class MongoAtlasVectorWrongMetricDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorWrongMetricDoc>;

[HostScoped]
public sealed class MongoAtlasVectorCoexistDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorCoexistDoc>
{
    public string Title { get; set; } = string.Empty;
}

[HostScoped]
public sealed class MongoAtlasVectorEuclideanDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorEuclideanDoc>;

[HostScoped]
public sealed class MongoAtlasVectorDotProductDoc : Koan.Data.Core.Model.Entity<MongoAtlasVectorDotProductDoc>;
