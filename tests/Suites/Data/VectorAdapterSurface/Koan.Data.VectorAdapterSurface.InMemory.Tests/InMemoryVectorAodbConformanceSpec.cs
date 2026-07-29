using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Connector.InMemory;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

/// <summary>
/// The Docker-free cell of the vector AODB conformance ledger (ARCH-0103 §6) — InMemoryVector, the in-process vector
/// floor, is the canonical co-definition: a real <c>AddKoan()</c> host with tenancy + the discoverable
/// <see cref="VectorConformanceShardAxis"/> proves the decorator <b>declares</b> all three isolation tokens AND realizes
/// all three modes (Shared overlay, Container partition-fold, Database source-fold), with no container to start. All
/// seven vector adapters now share this kit (ARCH-0103 §9.16); the live-Docker proof for the HTTP fleet lives in each
/// adapter's <c>*VectorAodbConformanceSpec</c> subclass.
/// </summary>
public sealed class InMemoryVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            // The Database-mode route resolves the source from the AMBIENT shard (DatabaseRouteRegistry), not from this
            // registry — InMemoryVector then folds that source key into its store name, so these entries are INERT for
            // the InMemory vector path. Kept to mirror VectorDatabaseRoutingSpec, where a name-folding-free adapter
            // (e.g. SqliteVec) instead needs a real per-source ConnectionString here.
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "inmemory",
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "inmemory",
            ["Koan:Data:Sources:VectorReadOnly:Adapter"] = "inmemory",
            ["Koan:Data:Sources:VectorReadOnly:Access"] = "ReadOnly",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan(koan =>
            {
                var shared = koan.Data.Source("VectorConformance");
                shared.Vector<VectorConformanceTenantDoc>(space => Space(space, "tenant"));
                shared.Vector<VectorConformancePartitionDoc>(space => Space(space, "partition"));
                koan.Data.Source("VectorReadOnly")
                    .Vector<InMemoryReadOnlyVectorDoc>(space => Space(space, "readonly"));
                koan.Data.Source(SourceA)
                    .Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
                koan.Data.Source(SourceB)
                    .Vector<VectorConformanceShardedDoc>(space => Space(space, "sharded"));
            }))
            .StartAsync()
            .ConfigureAwait(false);
        return (host, null);
    }

    protected override Task ProveVectorAnnexCellAsync(string acceptanceId, string proof) => acceptanceId switch
    {
        "V-01" => ProveSpacePlanAsync(),
        "V-02" => ProveEmbeddingBoundariesAsync(),
        "V-03" => ProveUpsertAsync(),
        "V-04" => ProveDeleteAsync(),
        "V-05" => ProveGetManyAsync(),
        "V-06" => ProveMetadataAsync(),
        "V-07" => ProveSearchOrderAsync(),
        "V-08" => ProveSimilarityAsync(),
        "V-09" => ProveSpaceIntegrityAsync(),
        "V-10" => ProveExecutionTruthAsync(),
        "V-11" => ProveSessionVisibilityAsync(),
        "V-12" => ProveEventualDeclineAsync(),
        "V-13" => ProveFilterAsync(),
        "V-14" => ProveHybridDeclineAsync(),
        "V-15" => ProveNamedSpaceDeclineAsync(),
        "V-16" => ProveContinuationDeclineAsync(),
        "V-17" => ProveBulkAsync(),
        "V-18" => ProveAtomicityTruthAsync(),
        "V-19" => ProveExportDeclineAsync(),
        "V-20" => ProveLifecyclePolicyAsync(),
        "V-21" => ProveIsolationAsync(),
        "V-22" => ProveCoordinationAsync(),
        "V-23" => ProveFailureLifecycleAsync(),
        "V-24" => ProveWarmPathAsync(),
        _ => base.ProveVectorAnnexCellAsync(acceptanceId, proof)
    };

    private static float[] Point(float x, float y = 0f) => [x, y, 0f, 0f, 0f, 0f, 0f, 0f];

    private IVectorSearchRepository<VectorConformancePartitionDoc, string> Repository() =>
        Host!.Services.GetRequiredService<IVectorService>()
            .TryGetRepository<VectorConformancePartitionDoc, string>()
        ?? throw new InvalidOperationException("InMemory Vector repository did not resolve.");

    private static object? Value(DataObject value, string name) =>
        Assert.Single(value.Properties, property => property.Name == name).Value;

    private async Task ProveSpacePlanAsync()
    {
        Assert.True(Vector<VectorConformancePartitionDoc>.IsAvailable);
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("bad-plan", [1f, 0f]));
        Assert.Contains("requires 8", error.Message, StringComparison.Ordinal);
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("bad-plan"));
    }

    private async Task ProveEmbeddingBoundariesAsync()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("empty", []));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("nan", [float.NaN, 0f, 0f, 0f, 0f, 0f, 0f, 0f]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("infinite", [float.PositiveInfinity, 0f, 0f, 0f, 0f, 0f, 0f, 0f]));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("nan"));
    }

    private async Task ProveUpsertAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(1), new { Version = 1 });
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1), new { Version = 2 });
        var point = Assert.IsType<VectorPoint<string>>(await Vector<VectorConformancePartitionDoc>.Get("same"));
        Assert.Equal(Point(0, 1), point.Embedding.ToArray());
        Assert.Equal(2, Value(Assert.IsType<DataObject>(point.Metadata), "Version"));
        Assert.Single((await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1), query => query.Top(10))).Items);
    }

    private async Task ProveDeleteAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("delete", Point(1));
        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("delete"));
        Assert.False(await Vector<VectorConformancePartitionDoc>.Delete("delete"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Vector<VectorConformancePartitionDoc>.Delete("delete", canceled.Token));
    }

    private async Task ProveGetManyAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("one", Point(1));
        var points = await Vector<VectorConformancePartitionDoc>.Get(["one", "missing", "one"]);
        Assert.Equal(3, points.Count);
        Assert.Equal("one", points[0]!.Id);
        Assert.Null(points[1]);
        Assert.Equal("one", points[2]!.Id);
    }

    private async Task ProveMetadataAsync()
    {
        var callerBytes = new byte[] { 1, 2, 3 };
        await Vector<VectorConformancePartitionDoc>.Save("meta", Point(1), new
        {
            Title = "neutral",
            Detail = new { Count = 2 },
            Tags = new[] { "a", "b" },
            Blob = callerBytes
        });
        callerBytes[0] = 9;
        var metadata = Assert.IsType<DataObject>((await Vector<VectorConformancePartitionDoc>.Get("meta"))!.Metadata);
        Assert.Equal("neutral", Value(metadata, "Title"));
        Assert.Equal(2, Value(Assert.IsType<DataObject>(Value(metadata, "Detail")), "Count"));
        Assert.Equal(2, Assert.IsType<DataArray>(Value(metadata, "Tags")).Items.Count);
        var returnedBytes = Assert.IsType<byte[]>(Value(metadata, "Blob"));
        Assert.Equal(1, returnedBytes[0]);
        returnedBytes[0] = 7;
        var reread = Assert.IsType<DataObject>((await Vector<VectorConformancePartitionDoc>.Get("meta"))!.Metadata);
        Assert.Equal(1, Assert.IsType<byte[]>(Value(reread, "Blob"))[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("reserved", Point(1),
                new Dictionary<string, object?> { ["__koan_user"] = "collision" }));
    }

    private async Task ProveSearchOrderAsync()
    {
        foreach (var id in new[] { "c", "a", "b" })
            await Vector<VectorConformancePartitionDoc>.Save(id, Point(1));
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(2));
        Assert.Equal(["a", "b"], result.Items.Select(item => item.Id));
        Assert.Equal(2, result.Items.Select(item => item.Id).Distinct().Count());
        Assert.True(result.Items[0].Similarity >= result.Items[1].Similarity);
    }

    private async Task ProveSimilarityAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        await Vector<VectorConformancePartitionDoc>.Save("orthogonal", Point(0, 1));
        await Vector<VectorConformancePartitionDoc>.Save("opposite", Point(-1));
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(3));
        Assert.All(result.Items, item => Assert.InRange(item.Similarity, 0d, 1d));
        Assert.Equal(["same", "orthogonal", "opposite"], result.Items.Select(item => item.Id));
        Assert.True(result.Items[0].Similarity > result.Items[1].Similarity);
        Assert.True(result.Items[1].Similarity > result.Items[2].Similarity);
    }

    private async Task ProveSpaceIntegrityAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("space", Point(1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Space("other")));
        Assert.Contains("Available space: partition", error.Message, StringComparison.Ordinal);
    }

    private async Task ProveExecutionTruthAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("one", Point(1));
        await Vector<VectorConformancePartitionDoc>.Save("two", Point(0, 1));
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));
        Assert.Equal(VectorSearchAccuracy.Exact, result.Execution.Accuracy);
        Assert.Equal(VectorMetric.Cosine, result.Execution.Metric);
        Assert.Equal(2, result.Execution.CandidatesConsidered);
        Assert.Null(result.Continuation);
    }

    private async Task ProveSessionVisibilityAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("visible", Point(1));
        Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("visible"));
        Assert.Contains((await Vector<VectorConformancePartitionDoc>.Search(Point(1))).Items,
            item => item.Id == "visible");
        Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("visible"));
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("visible"));
    }

    private Task ProveEventualDeclineAsync()
    {
        using var factory = new InMemoryVectorAdapterFactory(Options.Create(new InMemoryVectorOptions()));
        var plan = new VectorSpacePlan("eventual", "events", 8, VectorMetric.Cosine, VectorVisibility.Eventual);
        Assert.Throws<NotSupportedException>(() =>
            factory.Create<VectorConformancePartitionDoc, string>(Host!.Services, plan));
        return Task.CompletedTask;
    }

    private async Task ProveFilterAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("near-excluded", Point(1), new { Group = "other" });
        await Vector<VectorConformancePartitionDoc>.Save("far-included", Point(0, 1), new { Group = "wanted" });
        var result = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query
            .Top(1)
            .Where(Filter.Eq("Group", "wanted")));
        Assert.Equal("far-included", Assert.Single(result.Items).Id);
        Assert.Equal(1, result.Execution.CandidatesConsidered);
    }

    private async Task ProveHybridDeclineAsync()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.Hybrid));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Text("words").SemanticWeight(.5)));
    }

    private async Task ProveNamedSpaceDeclineAsync()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.MultiVectorPerEntity));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Space("undeclared")));
    }

    private async Task ProveContinuationDeclineAsync()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.NativeContinuation));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.After("not-a-snapshot")));
    }

    private async Task ProveBulkAsync()
    {
        var repository = Repository();
        var points = new[]
        {
            new VectorPoint<string>("a", Point(1), null),
            new VectorPoint<string>("b", Point(0, 1), null)
        };
        var saved = await repository.Save(points, VectorScope.Unscoped);
        Assert.Equal(["a", "b"], saved.Items.Select(item => item.Id));
        Assert.All(saved.Items, item => Assert.Equal(MutationOutcome.Inserted, item.Outcome));
        var deleted = await repository.Delete(["b", "missing", "a"], VectorScope.Unscoped);
        Assert.Equal([MutationOutcome.Deleted, MutationOutcome.Missing, MutationOutcome.Deleted],
            deleted.Items.Select(item => item.Outcome));
    }

    private async Task ProveAtomicityTruthAsync()
    {
        var result = await Repository().Save(
        [
            new VectorPoint<string>("a", Point(1), null),
            new VectorPoint<string>("b", Point(0, 1), null)
        ], VectorScope.Unscoped);
        Assert.Equal(BatchAtomicity.NotGuaranteed, result.Atomicity);
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.AtomicBatch));
    }

    private Task ProveExportDeclineAsync()
    {
        Assert.False(Vector<VectorConformancePartitionDoc>.GetCapabilities().Has(VectorCaps.StreamingResults));
        Assert.ThrowsAny<Exception>(() => Repository().ExportAll());
        return Task.CompletedTask;
    }

    private async Task ProveLifecyclePolicyAsync()
    {
        await Vector<VectorConformancePartitionDoc>.EnsureCreated();
        await Vector<VectorConformancePartitionDoc>.Sync();
        await Vector<VectorConformancePartitionDoc>.Save("clear", Point(1));
        await Vector<VectorConformancePartitionDoc>.Clear();
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("clear"));

        using (EntityContext.Source("VectorReadOnly"))
        {
            await Vector<InMemoryReadOnlyVectorDoc>.Sync();
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<InMemoryReadOnlyVectorDoc>.Save("blocked", Point(1)));
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<InMemoryReadOnlyVectorDoc>.Clear());
            await Assert.ThrowsAsync<DataSourcePolicyException>(() =>
                Vector<InMemoryReadOnlyVectorDoc>.EnsureCreated());
        }
    }

    private async Task ProveIsolationAsync()
    {
        using (Vector<VectorConformancePartitionDoc>.WithPartition("alpha"))
            await Vector<VectorConformancePartitionDoc>.Save("same", Point(1));
        using (Vector<VectorConformancePartitionDoc>.WithPartition("beta"))
            await Vector<VectorConformancePartitionDoc>.Save("same", Point(0, 1));

        using (Vector<VectorConformancePartitionDoc>.WithPartition("alpha"))
        {
            Assert.Equal(Point(1), (await Vector<VectorConformancePartitionDoc>.Get("same"))!.Embedding.ToArray());
            Assert.True(await Vector<VectorConformancePartitionDoc>.Delete("same"));
        }
        using (Vector<VectorConformancePartitionDoc>.WithPartition("beta"))
        {
            Assert.NotNull(await Vector<VectorConformancePartitionDoc>.Get("same"));
            Assert.Contains((await Vector<VectorConformancePartitionDoc>.Search(Point(0, 1))).Items,
                item => item.Id == "same");
        }
    }

    private async Task ProveCoordinationAsync()
    {
        var entity = new VectorConformancePartitionDoc { Id = "coordinated" };
        using (EntityContext.Transaction("vector-annex"))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Vector<VectorConformancePartitionDoc>.SaveWithVector(entity, Point(1)));
            Assert.Contains("does not claim cross-store transaction atomicity", error.Message, StringComparison.Ordinal);
        }
        Assert.Null(await Vector<VectorConformancePartitionDoc>.Get("coordinated"));
    }

    private async Task ProveFailureLifecycleAsync()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Vector<VectorConformancePartitionDoc>.Save("canceled", Point(1), ct: canceled.Token));

        var factory = new InMemoryVectorAdapterFactory(Options.Create(new InMemoryVectorOptions()));
        var repository = factory.Create<VectorConformancePartitionDoc, string>(Host!.Services,
            new VectorSpacePlan("disposed", "disposed", 8, VectorMetric.Cosine, VectorVisibility.Session));
        factory.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            repository.Save(new VectorPoint<string>("x", Point(1), null), VectorScope.Unscoped));
    }

    private async Task ProveWarmPathAsync()
    {
        await Vector<VectorConformancePartitionDoc>.Save("warm", Point(1), new { Group = "warm" });
        _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
        _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query.Top(1));

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 64; index++)
        {
            await Vector<VectorConformancePartitionDoc>.Save($"warm-{index}", Point(1), new { Group = "warm" });
            _ = await Vector<VectorConformancePartitionDoc>.Get("warm");
            _ = await Vector<VectorConformancePartitionDoc>.Search(Point(1), query => query
                .Top(4)
                .Where(Filter.Eq("Group", "warm")));
        }
        timer.Stop();
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(delta < 64 * 1024 * 1024, $"64 warm save/get/filter-search cycles allocated {delta:N0} bytes.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"64 warm cycles took {timer.Elapsed}.");
    }

    private static void Space<TEntity>(VectorSpaceBuilder<TEntity> space, string name)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string> => space
        .Name(name)
        .Dimensions(8)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session);
}

[HostScoped]
public sealed class InMemoryReadOnlyVectorDoc : Koan.Data.Core.Model.Entity<InMemoryReadOnlyVectorDoc> { }
