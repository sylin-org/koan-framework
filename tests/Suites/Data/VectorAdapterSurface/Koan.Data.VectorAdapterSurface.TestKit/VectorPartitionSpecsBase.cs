using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Vector;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Partition isolation specs. After Phase 1c.2.a moved naming end-to-end into adapter factories
/// (Weaviate's <c>_</c> separator, Couchbase's native scope, PGVector's <c>#</c> table suffix),
/// the only way to verify isolation is exercising real partition-scoped reads/writes through
/// <c>Vector&lt;T&gt;.WithPartition</c> against each adapter.
/// </summary>
public abstract class VectorPartitionSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IVectorAdapterTestFactory
{
    protected readonly TFactory Factory;
    private IDisposable? _scope;

    /// <summary>Partitions exercised by isolation specs. Override to extend, but at least 2 required.</summary>
    protected virtual IReadOnlyList<string> KnownPartitions { get; } = new[] { "alpha", "beta", "gamma" };

    protected VectorPartitionSpecsBase(TFactory factory) { Factory = factory; }

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        Koan.Data.Core.AggregateConfigs.Reset();
        // ResetAsync first (it may rebuild the SP), then push scope to the post-reset Services.
        await Factory.ResetAsync();
        _scope = AppHost.PushScope(Factory.Services);

        // Wipe shared (no-partition) namespace + every known partition.
        foreach (var p in KnownPartitions)
        {
            using (EntityContext.Partition(p))
            {
                try { await Vector<TodoVector>.EnsureCreated(); } catch { /* dynamic adapters */ }
                try { await Vector<TodoVector>.Flush(); } catch { /* if Flush is unsupported per partition the surface base already skips */ }
            }
        }
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return Task.CompletedTask;
    }

    protected void SkipIfUnsupported()
    {
        Skip.If(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");
        Skip.If(!Factory.SupportsPartitionIsolation, "Adapter does not support partition isolation.");
    }

    protected float[] Embed(string category, int seed) => EmbeddingFactory.ForCategory(category, seed, Factory.EmbeddingDimension);

    [SkippableFact]
    public async Task Upsert_inPartitionA_isInvisibleFromPartitionB()
    {
        SkipIfUnsupported();

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Save("only-in-alpha", Embed("anchor", 1));
        }

        using (Vector<TodoVector>.WithPartition("beta"))
        {
            var hits = await Vector<TodoVector>.Search(Embed("anchor", 1), topK: 10);
            hits.Matches.Select(m => m.Id).Should().NotContain("only-in-alpha");
        }
    }

    [SkippableFact]
    public async Task Search_inPartitionA_neverReturnsPartitionBResults()
    {
        SkipIfUnsupported();

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Save("alpha-1", Embed("anchor", 1));
            await Vector<TodoVector>.Save("alpha-2", Embed("anchor", 2));
        }
        using (Vector<TodoVector>.WithPartition("beta"))
        {
            await Vector<TodoVector>.Save("beta-1", Embed("anchor", 3));
            await Vector<TodoVector>.Save("beta-2", Embed("anchor", 4));
        }

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            var hits = await Vector<TodoVector>.Search(Embed("anchor", 0), topK: 10);
            hits.Matches.Select(m => m.Id).Should().OnlyContain(id => ((string)(object)id).StartsWith("alpha-"));
        }
    }

    [SkippableFact]
    public async Task Delete_inPartitionA_doesNotAffectPartitionB()
    {
        SkipIfUnsupported();

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Save("shared-id", Embed("anchor", 1));
        }
        using (Vector<TodoVector>.WithPartition("beta"))
        {
            await Vector<TodoVector>.Save("shared-id", Embed("anchor", 2));
        }

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Delete("shared-id");
        }

        using (Vector<TodoVector>.WithPartition("beta"))
        {
            var hits = await Vector<TodoVector>.Search(Embed("anchor", 0), topK: 5);
            hits.Matches.Select(m => m.Id).Should().Contain("shared-id");
        }
    }

    [SkippableFact]
    public async Task Flush_inPartitionA_doesNotAffectPartitionB()
    {
        Skip.If(!Factory.SupportsFlush, "Adapter does not implement Flush.");
        SkipIfUnsupported();

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Save("alpha-1", Embed("anchor", 1));
        }
        using (Vector<TodoVector>.WithPartition("beta"))
        {
            await Vector<TodoVector>.Save("beta-1", Embed("anchor", 2));
        }

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            await Vector<TodoVector>.Flush();
        }

        using (Vector<TodoVector>.WithPartition("beta"))
        {
            var hits = await Vector<TodoVector>.Search(Embed("anchor", 0), topK: 5);
            hits.Matches.Select(m => m.Id).Should().Contain("beta-1");
        }

        using (Vector<TodoVector>.WithPartition("alpha"))
        {
            var hits = await Vector<TodoVector>.Search(Embed("anchor", 0), topK: 5);
            hits.Matches.Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task ConcurrentWrites_acrossPartitions_remainIsolated()
    {
        SkipIfUnsupported();

        // Fire writes across all known partitions concurrently. Each lambda is wrapped in
        // Task.Run to genuinely parallelize them across threadpool threads; the ExecutionContext
        // (and therefore EntityContext + AppHost AsyncLocals) is captured at the point of Task.Run.
        var tasks = new List<Task>();
        for (int pi = 0; pi < KnownPartitions.Count; pi++)
        {
            var p = KnownPartitions[pi];
            var pIndex = pi;
            for (int i = 1; i <= 4; i++)
            {
                var iCopy = i;
                tasks.Add(Task.Run(async () =>
                {
                    using (Vector<TodoVector>.WithPartition(p))
                    {
                        await Vector<TodoVector>.Save($"{p}-{iCopy}", Embed(p, pIndex * 100 + iCopy));
                    }
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Each partition should see exactly its own writes.
        var diagnostics = new List<string>();
        foreach (var p in KnownPartitions)
        {
            using (Vector<TodoVector>.WithPartition(p))
            {
                var hits = await Vector<TodoVector>.Search(Embed(p, 0), topK: 50);
                var ids = hits.Matches.Select(m => (string)(object)m.Id).ToList();
                diagnostics.Add($"{p}: [{string.Join(", ", ids)}]");
                ids.Should().HaveCountGreaterThanOrEqualTo(4, $"partition '{p}' should hold its 4 writes. Diagnostics: {string.Join(" | ", diagnostics)}");
                ids.Should().OnlyContain(id => id.StartsWith($"{p}-"));
            }
        }
    }
}
