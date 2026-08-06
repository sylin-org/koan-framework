using System.Diagnostics;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Connector.Weaviate;
using Koan.Data.Vector.Naming;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>Live conformance suite for the Weaviate adapter.</summary>
public sealed class WeaviateVectorAodbConformanceSpec(WeaviateTestFactory fixture)
    : VectorAodbConformanceSpecsBase
{
    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        if (!fixture.IsAvailable) return (null, fixture.UnavailableReason);
        await fixture.Reset().ConfigureAwait(false);
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Weaviate:Endpoint"] = fixture.Endpoint,
            ["Koan:Data:Weaviate:DisableAutoDetection"] = "true",
            ["Koan:Data:Sources:VectorConformance:Adapter"] = "weaviate",
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "weaviate",
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "weaviate",
            ["Koan:Data:Sources:VectorReadOnly:Adapter"] = "weaviate",
            ["Koan:Data:Sources:VectorReadOnly:Access"] = "ReadOnly",
            ["Koan:Data:Sources:VectorExternal:Adapter"] = "weaviate",
            ["Koan:Data:Sources:VectorExternal:StorageLifecycle"] = "External"
        };
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                var shared = koan.Data.Source("VectorConformance");
                shared.Vector<VectorConformanceTenantDoc>(space => Space(space, "tenant"));
                shared.Vector<VectorConformancePartitionDoc>(space => Space(space, "partition"));
                shared.Vector<WeaviateEuclideanDoc>(space => Space(space, "euclidean", VectorMetric.Euclidean));
                shared.Vector<WeaviateDotProductDoc>(space => Space(space, "dot", VectorMetric.DotProduct));
                koan.Data.Source("VectorReadOnly").Vector<WeaviateReadOnlyDoc>(space => Space(space, "readonly"));
                koan.Data.Source("VectorExternal").Vector<WeaviateExternalDoc>(space => Space(space, "external"));
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
            "V-17" => OrderedBatch(),
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
        ?? throw new InvalidOperationException("Weaviate repository did not resolve.");

    private static object? Value(DataObject value, string name) =>
        Assert.Single(value.Properties, property => property.Name == name).Value;

    private async Task SpacePlan()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("bad-plan", [1f, 0f]));
        Assert.Contains("requires 8", error.Message, StringComparison.Ordinal);
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("bad-plan"));

        var factory = Host!.Services.GetRequiredService<WeaviateVectorAdapterFactory>();
        var logical = VectorAdapterNaming.GetOrCompute<WeaviateWrongShapeDoc>(Host.Services, factory, "Default");
        await fixture.PutCollection(WeaviateRepository<WeaviateWrongShapeDoc, string>.PhysicalName(logical), "wrong");
        var repository = factory.Create<WeaviateWrongShapeDoc, string>(Host.Services,
            new VectorSpacePlan("Default", "wrong", 8, VectorMetric.Cosine, VectorVisibility.Session));
        var shape = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.VectorEnsureCreated());
        Assert.Contains("contract marker differs", shape.Message, StringComparison.Ordinal);
    }

    private async Task EmbeddingBoundaries()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("empty", []));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("nan", Point(float.NaN)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("infinite", Point(float.PositiveInfinity)));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("zero", Point(0)));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("nan"));
    }

    private async Task Upsert()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(2), new { Version = 1 });
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 3), new { Version = 2 });
        var point = Assert.IsType<VectorPoint<string>>(await Vector<VectorConformancePartitionDoc>.Get("same"));
        Assert.Equal(Point(0, 3), point.Embedding.ToArray(), new FloatToleranceComparer(0.00001f));
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(point.Metadata), "Version")));
        Assert.Single((await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1), query => query.Top(10))).Items);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("reserved", Point(1),
                new Dictionary<string, object?> { ["__koan_user"] = "reserved" }));
        await Vector<VectorConformancePartitionDoc>.Save("meta", Point(1), new Dictionary<string, object?>
        {
            ["Title"] = "neutral",
            ["Detail"] = new Dictionary<string, object?> { ["Count"] = 2 },
            ["Tags"] = new[] { "a", "b" },
            ["Blob"] = bytes,
            ["user__koan"] = "preserved"
        });
        bytes[0] = 9;
        var metadata = Assert.IsType<DataObject>((await Vector<VectorConformancePartitionDoc>.Get("meta"))!.Metadata);
        Assert.Equal("neutral", Value(metadata, "Title"));
        Assert.Equal(2L, Convert.ToInt64(Value(Assert.IsType<DataObject>(Value(metadata, "Detail")), "Count")));
        Assert.Equal(2, Assert.IsType<DataArray>(Value(metadata, "Tags")).Items.Count);
        Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(Value(metadata, "Blob")));
        Assert.Equal("preserved", Value(metadata, "user__koan"));
    }

    private async Task SearchOrder()
    {
        foreach (var id in new[] { "c", "a", "b" }) await Vector<VectorConformancePartitionDoc>.Save(id, Point(1));
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

        await Vector<WeaviateEuclideanDoc>.Save("same", Point(0));
        await Vector<WeaviateEuclideanDoc>.Save("five-away", Point(3, 4));
        var euclidean = await Vector<WeaviateEuclideanDoc>.Search(Point(0), query => query.Top(2));
        Assert.Equal(1d, euclidean.Items[0].Similarity, 10);
        Assert.Equal(1d / 6d, euclidean.Items[1].Similarity, 5);

        await Vector<WeaviateDotProductDoc>.Save("positive", Point(2));
        await Vector<WeaviateDotProductDoc>.Save("zero", Point(0, 1));
        await Vector<WeaviateDotProductDoc>.Save("negative", Point(-1));
        var dot = await Vector<WeaviateDotProductDoc>.Search(Point(1), query => query.Top(3));
        Assert.Equal(["positive", "zero", "negative"], dot.Items.Select(item => item.Id));
        Assert.True(dot.Items[0].Similarity > dot.Items[1].Similarity);
        Assert.True(dot.Items[1].Similarity > dot.Items[2].Similarity);
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
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));
        Assert.Equal(VectorSearchAccuracy.Approximate, result.Execution.Accuracy);
        Assert.Equal(VectorMetric.Cosine, result.Execution.Metric);
        Assert.Null(result.Execution.CandidatesConsidered);
        Assert.Null(result.Continuation);
    }

    private async Task SessionVisibility()
    {
        await Vector<VectorConformancePartitionDoc>.Save("visible", Point(1));
        Assert.Contains((await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(10))).Items,
            item => item.Id == "visible");
        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("visible"));
        Assert.DoesNotContain((await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(10))).Items,
            item => item.Id == "visible");
    }

    private Task EventualDecline()
    {
        var factory = Host!.Services.GetRequiredService<WeaviateVectorAdapterFactory>();
        Assert.Throws<NotSupportedException>(() => factory.Create<VectorConformancePartitionDoc, string>(Host.Services,
            new VectorSpacePlan("eventual", "events", 8, VectorMetric.Cosine, VectorVisibility.Eventual)));
        return Task.CompletedTask;
    }

    private async Task NativeFilter()
    {
        var corpus = new (string Id, Dictionary<string, object?> Metadata)[]
        {
            ("a", new() { ["Category"] = "legal", ["Tags"] = new[] { "x", "y" } }),
            ("b", new() { ["Category"] = "legal", ["Tags"] = new[] { "y", "z" } }),
            ("c", new() { ["Category"] = "finance", ["Tags"] = new[] { "x" } }),
            ("d", new() { ["Category"] = "finance" }),
            ("e", new() { ["Tags"] = new[] { "z" } })
        };
        foreach (var item in corpus) await Vector<VectorConformancePartitionDoc>.Save(item.Id, Point(1), item.Metadata);
        static Filter Leaf(string field, FilterOperator operation, object? value) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Of(value));
        static Filter Set(string field, FilterOperator operation, params object?[] values) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Many(values));
        Filter[] cases =
        [
            Filter.Eq("Category", "legal"), Leaf("Category", FilterOperator.Ne, "legal"),
            Filter.In("Category", ["legal", "finance"]), Set("Category", FilterOperator.Nin, "legal"),
            Leaf("Tags", FilterOperator.Has, "x"), Set("Tags", FilterOperator.HasAny, "z", "x"),
            Set("Tags", FilterOperator.HasAll, "x", "y"), Set("Tags", FilterOperator.HasNone, "x"),
            Leaf("Tags", FilterOperator.Size, 2), Leaf("Category", FilterOperator.Exists, true),
            Leaf("Category", FilterOperator.Exists, false),
            Filter.All(Filter.Eq("Category", "legal"), Leaf("Tags", FilterOperator.Has, "z")),
            Filter.Any(Filter.Eq("Category", "finance"), Leaf("Tags", FilterOperator.Has, "z")),
            Filter.Negate(Filter.Eq("Category", "legal"))
        ];
        foreach (var filter in cases)
        {
            var expected = corpus.Where(item => DictionaryFilterEvaluator.Compile(filter)(item.Metadata))
                .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var actual = (await Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(100).Where(filter))).Items.Select(item => item.Id);
            Assert.True(expected.SetEquals(actual), $"Filter {filter} did not converge with the neutral oracle.");
        }
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Category", FilterOperator.Gt, "a"))));
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

    private async Task OrderedBatch()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.BulkUpsert));
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.BulkDelete));
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
            await Vector<WeaviateReadOnlyDoc>.Sync();
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<WeaviateReadOnlyDoc>.Save("blocked", Point(1)));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<WeaviateReadOnlyDoc>.Clear());
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<WeaviateReadOnlyDoc>.EnsureCreated());
        }
        using (EntityContext.Source("VectorExternal"))
        {
            Assert.Null(await Vector<WeaviateExternalDoc>.Get("missing"));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<WeaviateExternalDoc>.EnsureCreated());
        }
    }

    private async Task Isolation()
    {
        using (EntityContext.Partition("alpha")) await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        using (EntityContext.Partition("beta")) await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1));
        using (EntityContext.Partition("alpha"))
        {
            Assert.Equal(Point(1), (await Vector<VectorConformancePartitionDoc>.Get("same"))!.Embedding.ToArray(),
                new FloatToleranceComparer(0.00001f));
            Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("same"));
        }
        using (EntityContext.Partition("beta")) Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("same"));
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
        await fixture.Restart();
        Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("durable"));
        var factory = Host!.Services.GetRequiredService<WeaviateVectorAdapterFactory>();
        var repository = factory.Create<VectorConformancePartitionDoc, string>(Host.Services,
            new VectorSpacePlan("disposed", "disposed", 8, VectorMetric.Cosine, VectorVisibility.Session));
        await ((IAsyncDisposable)repository).DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            repository.Save(new VectorPoint<string>("x", Point(1), null), VectorScope.Unscoped));
    }

    private async Task WarmPath()
    {
        await Vector<VectorConformancePartitionDoc>.Save("warm", Point(1));
        _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 8; index++)
        {
            await Vector<VectorConformancePartitionDoc>.Save($"warm-{index}", Point(1));
            _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
            _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(4));
        }
        timer.Stop();
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(delta < 64 * 1024 * 1024, $"Eight network warm cycles allocated {delta:N0} bytes.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Eight network warm cycles took {timer.Elapsed}.");
    }

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
public sealed class WeaviateReadOnlyDoc : Koan.Data.Core.Model.Entity<WeaviateReadOnlyDoc>;
[HostScoped]
public sealed class WeaviateExternalDoc : Koan.Data.Core.Model.Entity<WeaviateExternalDoc>;
public sealed class WeaviateWrongShapeDoc : Koan.Data.Core.Model.Entity<WeaviateWrongShapeDoc>;
[HostScoped]
public sealed class WeaviateEuclideanDoc : Koan.Data.Core.Model.Entity<WeaviateEuclideanDoc>;
[HostScoped]
public sealed class WeaviateDotProductDoc : Koan.Data.Core.Model.Entity<WeaviateDotProductDoc>;
