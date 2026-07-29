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
using Koan.Data.Vector.Connector.SqliteVec;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

/// <summary>Executable DAC-50 ledger for the stable sqlite-vec reference adapter.</summary>
public sealed class SqliteVecVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private string? _root;
    private string? _externalPath;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "koan-sqlitevec-conformance", Guid.NewGuid().ToString("N"));
        _externalPath = Path.Combine(_root, "external-missing.db");
        var database = Path.Combine(_root, "vectors.db");
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Sources:VectorConformance:Adapter"] = "sqlitevec",
            ["Koan:Data:Sources:VectorConformance:SqliteVec:ConnectionString"] = $"Data Source={database};Pooling=False",
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "sqlitevec",
            [$"Koan:Data:Sources:{SourceA}:SqliteVec:ConnectionString"] = $"Data Source={database};Pooling=False",
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "sqlitevec",
            [$"Koan:Data:Sources:{SourceB}:SqliteVec:ConnectionString"] = $"Data Source={database};Pooling=False",
            ["Koan:Data:Sources:VectorReadOnly:Adapter"] = "sqlitevec",
            ["Koan:Data:Sources:VectorReadOnly:Access"] = "ReadOnly",
            ["Koan:Data:Sources:VectorReadOnly:SqliteVec:ConnectionString"] = $"Data Source={database};Pooling=False",
            ["Koan:Data:Sources:VectorExternal:Adapter"] = "sqlitevec",
            ["Koan:Data:Sources:VectorExternal:StorageLifecycle"] = "External",
            ["Koan:Data:Sources:VectorExternal:SqliteVec:ConnectionString"] = $"Data Source={_externalPath};Pooling=False"
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                var shared = koan.Data.Source("VectorConformance");
                shared.Vector<VectorConformanceTenantDoc>(space => Space(space, "tenant"));
                shared.Vector<VectorConformancePartitionDoc>(space => Space(space, "partition"));
                koan.Data.Source("VectorReadOnly").Vector<SqliteVecReadOnlyDoc>(space => Space(space, "readonly"));
                koan.Data.Source("VectorExternal").Vector<SqliteVecExternalDoc>(space => Space(space, "external"));
                koan.Data.Source(SourceA).Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
                koan.Data.Source(SourceB).Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
            }))
            .StartAsync()
            .ConfigureAwait(false);
        return (host, null);
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        await ValueTask.CompletedTask;
    }

    protected override Task ProveVectorAnnexCellAsync(string acceptanceId, string proof) => acceptanceId switch
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
        "V-13" => FilterDecline(),
        "V-14" => HybridDecline(),
        "V-15" => NamedSpaceDecline(),
        "V-16" => ContinuationDecline(),
        "V-17" => Bulk(),
        "V-18" => AtomicBatch(),
        "V-19" => ExportDecline(),
        "V-20" => LifecyclePolicy(),
        "V-21" => Isolation(),
        "V-22" => Coordination(),
        "V-23" => FailureLifecycle(),
        "V-24" => WarmPath(),
        _ => base.ProveVectorAnnexCellAsync(acceptanceId, proof)
    };

    private static float[] Point(float x, float y = 0f) => [x, y, 0f, 0f, 0f, 0f, 0f, 0f];

    private IVectorSearchRepository<VectorConformancePartitionDoc, string> Repository() =>
        Host!.Services.GetRequiredService<IVectorService>()
            .TryGetRepository<VectorConformancePartitionDoc, string>()
        ?? throw new InvalidOperationException("SqliteVec repository did not resolve.");

    private static object? Value(DataObject value, string name) =>
        Assert.Single(value.Properties, property => property.Name == name).Value;

    private async Task SpacePlan()
    {
        Assert.True(Vector<VectorConformancePartitionDoc>.IsAvailable);
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("bad-plan", [1f, 0f]));
        Assert.Contains("requires 8", error.Message, StringComparison.Ordinal);
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("bad-plan"));
    }

    private async Task EmbeddingBoundaries()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("empty", []));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("nan", Point(float.NaN)));
        await Assert.ThrowsAsync<ArgumentException>(() => Vector<VectorConformancePartitionDoc>.Save("infinite", Point(float.PositiveInfinity)));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("nan"));
    }

    private async Task Upsert()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(1), new { Version = 1 });
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1), new { Version = 2 });
        var point = Assert.IsType<VectorPoint<string>>(await Vector<VectorConformancePartitionDoc>.Get("same"));
        Assert.Equal(Point(0, 1), point.Embedding.ToArray());
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
        Assert.True(result.Items[0].Similarity > result.Items[1].Similarity);
        Assert.True(result.Items[1].Similarity > result.Items[2].Similarity);
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
        Assert.Equal(VectorSearchAccuracy.Exact, result.Execution.Accuracy);
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
        var factory = Host!.Services.GetRequiredService<SqliteVecAdapterFactory>();
        var plan = new VectorSpacePlan("eventual", "events", 8, VectorMetric.Cosine, VectorVisibility.Eventual);
        Assert.Throws<NotSupportedException>(() =>
            factory.Create<VectorConformancePartitionDoc, string>(Host.Services, plan));
        return Task.CompletedTask;
    }

    private async Task FilterDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.Filters));
        await Assert.ThrowsAsync<VectorFilterUnsupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Where(Filter.Eq("Group", "wanted"))));
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
        var saved = await Repository().Save([
            new VectorPoint<string>("a", Point(1), null),
            new VectorPoint<string>("b", Point(0, 1), null)
        ], VectorScope.Unscoped);
        Assert.Equal([MutationOutcome.Inserted, MutationOutcome.Inserted], saved.Items.Select(item => item.Outcome));
        var deleted = await Repository().Delete(["b", "missing", "a"], VectorScope.Unscoped);
        Assert.Equal([MutationOutcome.Deleted, MutationOutcome.Missing, MutationOutcome.Deleted],
            deleted.Items.Select(item => item.Outcome));
    }

    private async Task AtomicBatch()
    {
        var repository = Repository();
        await Assert.ThrowsAsync<ArgumentException>(() => repository.Save([
            new VectorPoint<string>("valid", Point(1), null),
            new VectorPoint<string>("invalid", new float[] { 1f }, null)
        ], VectorScope.Unscoped));
        Assert.Null(await repository.Get("valid", VectorScope.Unscoped));
        var result = await repository.Save([new VectorPoint<string>("atomic", Point(1), null)], VectorScope.Unscoped);
        Assert.Equal(BatchAtomicity.Atomic, result.Atomicity);
        Assert.True(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.AtomicBatch));
    }

    private Task ExportDecline()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.StreamingResults));
        Assert.ThrowsAny<Exception>(() => Repository().ExportAll());
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
            await Vector<SqliteVecReadOnlyDoc>.Sync();
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<SqliteVecReadOnlyDoc>.Save("blocked", Point(1)));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<SqliteVecReadOnlyDoc>.Clear());
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<SqliteVecReadOnlyDoc>.EnsureCreated());
        }

        using (EntityContext.Source("VectorExternal"))
        {
            Assert.Null(await Vector<SqliteVecExternalDoc>.Get("missing"));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() => Vector<SqliteVecExternalDoc>.EnsureCreated());
        }
        Assert.False(File.Exists(_externalPath), "an External read or rejected ensure must not create its source");
    }

    private async Task Isolation()
    {
        using (EntityContext.Partition("alpha")) await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        using (EntityContext.Partition("beta")) await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1));
        using (EntityContext.Partition("alpha"))
        {
            Assert.Equal(Point(1), (await Vector<VectorConformancePartitionDoc>.Get("same"))!.Embedding.ToArray());
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

        var factory = Host!.Services.GetRequiredService<SqliteVecAdapterFactory>();
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
        for (var index = 0; index < 32; index++)
        {
            await Vector<VectorConformancePartitionDoc>.Save($"warm-{index}", Point(1));
            _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
            _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(4));
        }
        timer.Stop();
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(delta < 64 * 1024 * 1024, $"32 native warm cycles allocated {delta:N0} bytes.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"32 native warm cycles took {timer.Elapsed}.");
    }

    private static void Space<TEntity>(VectorSpaceBuilder<TEntity> space, string name)
        where TEntity : class, IEntity<string> => space
        .Name(name)
        .Dimensions(8)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session);
}

[HostScoped]
public sealed class SqliteVecReadOnlyDoc : Koan.Data.Core.Model.Entity<SqliteVecReadOnlyDoc>;

[HostScoped]
public sealed class SqliteVecExternalDoc : Koan.Data.Core.Model.Entity<SqliteVecExternalDoc>;
