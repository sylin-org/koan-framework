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
using Koan.Data.Vector.Connector.Chroma;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Chroma.Tests;

/// <summary>Live conformance suite for the Chroma network adapter (chromadb/chroma:1.5.9, REST v2).
/// Declined cells follow the Qdrant precedent: Chroma has no lexical index Koan can claim portable
/// hybrid semantics over (V-14), no multi-vector-per-entity (V-15), no stable continuation snapshot
/// (V-16), no atomic multi-point batch (V-18, upsert is per-point atomic only), and no streaming
/// export (V-19); Eventual visibility is not simulated (V-12).</summary>
public sealed class ChromaVectorAodbConformanceSpec(ChromaTestFactory fixture)
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
            ["Koan:Data:Chroma:Endpoint"] = fixture.Endpoint,
            ["Koan:Data:Chroma:DisableAutoDetection"] = "true",
            ["Koan:Data:Sources:VectorConformance:Adapter"] = "chroma",
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "chroma",
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "chroma",
            ["Koan:Data:Sources:VectorReadOnly:Adapter"] = "chroma",
            ["Koan:Data:Sources:VectorReadOnly:Access"] = "ReadOnly",
            ["Koan:Data:Sources:VectorExternal:Adapter"] = "chroma",
            ["Koan:Data:Sources:VectorExternal:StorageLifecycle"] = "External"
        };
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                var shared = koan.Data.Source("VectorConformance");
                shared.Vector<VectorConformanceTenantDoc>(space => Space(space, "tenant"));
                shared.Vector<VectorConformancePartitionDoc>(space => Space(space, "partition"));
                shared.Vector<ChromaEuclideanDoc>(space => Space(space, "euclidean", VectorMetric.Euclidean));
                shared.Vector<ChromaDotProductDoc>(space => Space(space, "dot", VectorMetric.DotProduct));
                koan.Data.Source("VectorReadOnly").Vector<ChromaReadOnlyDoc>(space => Space(space, "readonly"));
                koan.Data.Source("VectorExternal").Vector<ChromaExternalDoc>(space => Space(space, "external"));
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
        ?? throw new InvalidOperationException("Chroma repository did not resolve.");

    private static object? Value(DataObject value, string name) =>
        Assert.Single(value.Properties, property => property.Name == name).Value;

    private async Task SpacePlan()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("bad-plan", [1f, 0f]));
        Assert.Contains("requires 8", error.Message, StringComparison.Ordinal);
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("bad-plan"));

        // Chroma pins a collection's dimension from its first write; a pre-provisioned 4-dimensional
        // collection must be refused, not silently reused, when the plan declares 8.
        await fixture.PutWrongShapeCollection("ChromaWrongShapeDoc", 4);
        var factory = Host!.Services.GetRequiredService<ChromaVectorAdapterFactory>();
        var repository = factory.Create<ChromaWrongShapeDoc, string>(Host.Services,
            new VectorSpacePlan("Default", "wrong", 8, VectorMetric.Cosine, VectorVisibility.Session));
        var shape = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.VectorEnsureCreated());
        Assert.Contains("dimension is 4", shape.Message, StringComparison.Ordinal);
    }

    private async Task EmbeddingBoundaries()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("empty", []));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("nan", Point(float.NaN)));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("infinite", Point(float.PositiveInfinity)));
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
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(3));
        Assert.Equal(["same", "orthogonal", "opposite"], result.Items.Select(item => item.Id));
        // Chroma cosine distance is 1 - similarity: 0 / 1 / 2 for these unit vectors.
        Assert.Equal(1d, result.Items[0].Similarity, 10);
        Assert.Equal(0.5d, result.Items[1].Similarity, 10);
        Assert.True(result.Items[1].Similarity > result.Items[2].Similarity);

        await Vector<ChromaEuclideanDoc>.Save("same", Point(0));
        await Vector<ChromaEuclideanDoc>.Save("five-away", Point(3, 4));
        var euclidean = await Vector<ChromaEuclideanDoc>.Search(Point(0), query => query.Top(2));
        Assert.Equal(["same", "five-away"], euclidean.Items.Select(item => item.Id));
        Assert.Equal(1d, euclidean.Items[0].Similarity, 10);
        // Chroma's l2 space returns SQUARED euclidean distance: 3-4-5 away is distance 25.
        Assert.Equal(1d / 26d, euclidean.Items[1].Similarity, 5);
        Assert.Equal(VectorMetric.Euclidean, euclidean.Execution.Metric);

        await Vector<ChromaDotProductDoc>.Save("positive", Point(2));
        await Vector<ChromaDotProductDoc>.Save("zero", Point(0, 1));
        await Vector<ChromaDotProductDoc>.Save("negative", Point(-1));
        var dot = await Vector<ChromaDotProductDoc>.Search(Point(1), query => query.Top(3));
        Assert.Equal(["positive", "zero", "negative"], dot.Items.Select(item => item.Id));
        // Chroma ip distance is 1 - inner product; the logistic normalization is monotonic in it.
        Assert.Equal(0.5d, dot.Items[1].Similarity, 10);
        Assert.True(dot.Items[0].Similarity > dot.Items[1].Similarity);
        Assert.True(dot.Items[1].Similarity > dot.Items[2].Similarity);
        Assert.Equal(VectorMetric.DotProduct, dot.Execution.Metric);
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
        Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("visible"));
        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("visible"));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("visible"));
    }

    private Task EventualDecline()
    {
        var factory = Host!.Services.GetRequiredService<ChromaVectorAdapterFactory>();
        var plan = new VectorSpacePlan("eventual", "events", 8, VectorMetric.Cosine, VectorVisibility.Eventual);
        Assert.Throws<NotSupportedException>(() =>
            factory.Create<VectorConformancePartitionDoc, string>(Host.Services, plan));
        return Task.CompletedTask;
    }

    private async Task NativeFilter()
    {
        var corpus = new (string Id, Dictionary<string, object?> Metadata)[]
        {
            ("a", new() { ["Category"] = "legal", ["Priority"] = 1L }),
            ("b", new() { ["Category"] = "legal", ["Priority"] = 3L }),
            ("c", new() { ["Category"] = "finance", ["Priority"] = 2L }),
            ("d", new() { ["Category"] = "finance", ["Priority"] = 5L }),
            ("e", new() { ["Priority"] = 4L })
        };
        foreach (var item in corpus)
            await Vector<VectorConformancePartitionDoc>.Save(item.Id, Point(1), item.Metadata);

        static Filter Leaf(string field, FilterOperator operation, object? value) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Of(value));
        static Filter Set(string field, FilterOperator operation, params object?[] values) =>
            Filter.On(FieldPath.Of(field), operation, FilterValue.Many(values));
        // The proven pushdown set: scalar equality, inequality and ranges (absent keys agree with the
        // neutral evaluator — Ne/Nin match absent, ranges do not), membership sets, and and/or groups.
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
            Filter.All(Filter.Eq("Category", "legal"), Leaf("Priority", FilterOperator.Gte, 3L)),
            Filter.Any(Filter.Eq("Category", "finance"), Leaf("Priority", FilterOperator.Lte, 1L))
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

        // Everything Chroma's where-language cannot express rejects correctively before provider I/O.
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Category", FilterOperator.StartsWith, "fin"))));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Filter.Negate(Filter.Eq("Category", "legal")))));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Category", FilterOperator.Exists, true))));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Priority", FilterOperator.Gt, "a"))));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Detail.Count", FilterOperator.Eq, 2))));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(
                Point(1), query => query.Top(10).Where(Leaf("Category", FilterOperator.Eq, null))));
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
        var result = await repository.Save([new VectorPoint<string>("accepted", Point(1), null)], VectorScope.Unscoped);
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
            await Vector<ChromaReadOnlyDoc>.Sync();
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<ChromaReadOnlyDoc>.Save("blocked", Point(1)));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<ChromaReadOnlyDoc>.Clear());
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<ChromaReadOnlyDoc>.EnsureCreated());
        }
        using (EntityContext.Source("VectorExternal"))
        {
            Assert.Null(await Vector<ChromaExternalDoc>.Get("missing"));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<ChromaExternalDoc>.EnsureCreated());
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

        var factory = Host!.Services.GetRequiredService<ChromaVectorAdapterFactory>();
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
        for (var index = 0; index < 16; index++)
        {
            await Vector<VectorConformancePartitionDoc>.Save($"warm-{index}", Point(1));
            _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
            _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(4));
        }
        timer.Stop();
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(delta < 64 * 1024 * 1024, $"16 network warm cycles allocated {delta:N0} bytes.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"16 network warm cycles took {timer.Elapsed}.");
    }

    private static void Space<TEntity>(VectorSpaceBuilder<TEntity> space, string name,
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
public sealed class ChromaReadOnlyDoc : Koan.Data.Core.Model.Entity<ChromaReadOnlyDoc>;

[HostScoped]
public sealed class ChromaExternalDoc : Koan.Data.Core.Model.Entity<ChromaExternalDoc>;

public sealed class ChromaWrongShapeDoc : Koan.Data.Core.Model.Entity<ChromaWrongShapeDoc>;
[HostScoped]
public sealed class ChromaEuclideanDoc : Koan.Data.Core.Model.Entity<ChromaEuclideanDoc>;
[HostScoped]
public sealed class ChromaDotProductDoc : Koan.Data.Core.Model.Entity<ChromaDotProductDoc>;
