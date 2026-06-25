using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0103 P1 (the Moniker contract) — a <b>Database-mode</b> <c>[DataAxis]</c> auto-routes the VECTOR plane to a
/// distinct physical store per ambient shard, exactly as it already routes the record plane
/// (<see cref="MultiDatabaseRoutingSpec"/>). Proven Docker-free on the two in-process vector adapters: InMemoryVector
/// (a distinct in-memory store per source) and SqliteVec (a distinct <c>.db</c> file per source).
///
/// <para>The footgun this gate catches is the vector/record <b>split-brain</b> (ARCH-0103 §2): before P1,
/// <c>VectorService</c> never consulted the route — so a Database-mode tenant's embeddings all landed in one shared
/// store (no isolation, no error) while the record write went to the tenant DB. Physical isolation is asserted by
/// <c>GetEmbedding</c> across shards (each shard sees only its own vector; the other's id resolves <c>null</c>) and a
/// cross-shard search id-set check. No explicit <c>EntityContext.Source</c> is ever set — only the ambient shard.</para>
/// </summary>
public sealed class VectorDatabaseRoutingSpec : IAsyncLifetime
{
    // Two [Sharded] vector entities, each pinned to one in-proc adapter via [VectorAdapter], so both adapters are
    // exercised in one host without the data-provider-derived election picking one for both.
    [Sharded]
    [VectorAdapter("inmemory")]
    public sealed class MemVec : Entity<MemVec> { }

    [Sharded]
    [VectorAdapter("sqlitevec")]
    public sealed class SqlVec : Entity<SqlVec> { }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"koan-vec-multidb-{Guid.CreateVersion7():n}");
    private IntegrationHost? _host;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",

            // A clean default SqliteVec file (used only by the pre-fix split-brain path; the per-source fix never reads it).
            ["Koan:Data:SqliteVec:ConnectionString"] = ConnVec("default"),

            // Distinct stores per shard — the source's ConnectionString IS the per-source SqliteVec store (the per-source
            // physical resolution reuses AdapterConnectionResolver, the same primitive the record plane uses). The
            // Adapter key makes the source discoverable; InMemoryVector ignores both and isolates by the source key alone.
            ["Koan:Data:Sources:tenant_a:Adapter"] = "sqlite",
            ["Koan:Data:Sources:tenant_a:ConnectionString"] = ConnVec("tenant_a"),
            ["Koan:Data:Sources:tenant_b:Adapter"] = "sqlite",
            ["Koan:Data:Sources:tenant_b:ConnectionString"] = ConnVec("tenant_b"),
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best-effort; sqlite-vec may hold a file lock until process exit */ }
    }

    private string ConnVec(string name) => $"Data Source={Path.Combine(_root, name + "_vec.db")}";

    [Fact(DisplayName = "InMemoryVector: a Database-mode axis routes embeddings to a distinct in-memory store per shard")]
    public Task InMemory_vector_isolates_by_ambient_shard() => AssertVectorShardIsolation<MemVec>();

    [Fact(DisplayName = "SqliteVec: a Database-mode axis routes embeddings to a distinct .db file per shard")]
    public Task SqliteVec_isolates_by_ambient_shard() => AssertVectorShardIsolation<SqlVec>();

    private static async Task AssertVectorShardIsolation<T>() where T : class, IEntity<string>
    {
        var ea = new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        var eb = new[] { 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f };

        // No EntityContext.Source anywhere — only the ambient shard. The Database-mode axis derives the source for BOTH
        // the (absent here) record plane and the vector plane; this proves the vector plane now honors it.
        using (ShardAmbient.Use("tenant_a")) await Vector<T>.Save("a", ea);
        using (ShardAmbient.Use("tenant_b")) await Vector<T>.Save("b", eb);

        using (ShardAmbient.Use("tenant_a"))
        {
            (await Vector<T>.GetEmbedding("a")).Should().NotBeNull();
            (await Vector<T>.GetEmbedding("b")).Should().BeNull();   // tenant_b's vector is unreachable from tenant_a
            (await Vector<T>.Search(ea, topK: 10)).Matches.Select(m => m.Id).Should().Equal("a");
        }

        using (ShardAmbient.Use("tenant_b"))
        {
            (await Vector<T>.GetEmbedding("b")).Should().NotBeNull();
            (await Vector<T>.GetEmbedding("a")).Should().BeNull();   // and vice-versa
            (await Vector<T>.Search(eb, topK: 10)).Matches.Select(m => m.Id).Should().Equal("b");
        }
    }
}
