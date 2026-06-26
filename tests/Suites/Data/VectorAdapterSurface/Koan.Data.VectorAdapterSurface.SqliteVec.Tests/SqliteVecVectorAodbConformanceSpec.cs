using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

/// <summary>
/// The second Docker-free cell of the vector AODB conformance ledger (ARCH-0103 §6) — sqlite-vec, the durable in-process
/// vector floor. It is the <b>non-filtering twin</b> of the InMemoryVector cell and the FIRST real adapter to exercise
/// the Shared cell's <i>under-claim / fail-closed</i> branch: sqlite-vec is a pure-KNN store (it announces no
/// <c>VectorCaps.Filters</c>), so the <see cref="Koan.Data.Vector.ScopedVectorRepository"/> decorator honestly does NOT
/// declare <c>RowScoped</c>, and a scoped read must FAIL CLOSED (<see cref="NotSupportedException"/>) rather than leak a
/// neighbouring tenant's vector. InMemoryVector takes the opposite (isolates) branch because it filters — together the
/// two cells co-define <c>RowScoped</c> from both sides.
/// <para>
/// The Container and Database modes are realized by mechanism, not name-fold: Container = a distinct <c>vec0</c> virtual
/// table per ambient partition inside one SQLite database; Database = a distinct SQLite database per routed source,
/// resolved at the factory through the shared <c>AdapterConnectionResolver.ResolveRoutedConnection</c>. The two
/// conformance sources therefore need a real per-source connection string (unlike the name-folding InMemory cell, where
/// they are inert). Each uses a private in-memory database (<c>Data Source=:memory:</c> is unshared per connection, and
/// sqlite-vec holds one connection per repository for its lifetime), so the isolation is genuine — no files, no cleanup,
/// fresh per test — and exercises the exact production per-source ROUTING path (factory →
/// <c>AdapterConnectionResolver.ResolveRoutedConnection</c> → a per-source repo); only the connection <i>target</i>
/// differs (a private <c>:memory:</c> database vs a file). The isolating mechanism here is connection-identity, not
/// string distinctness — both sources resolve the same <c>:memory:</c> string, but each routed source yields a distinct
/// repository (VectorService caches per source) holding its own private database, so a routing collapse would land both
/// shards in one database and fail the kNN exact-set assertion (the resolver's string-distinctness / <c>"auto"</c>
/// collapse is proved separately by the Mongo/Couchbase folds).
/// </para>
/// </summary>
public sealed class SqliteVecVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        const string Memory = "Data Source=:memory:";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            // The Default source (Shared + Container cells) — keep it off-disk too.
            ["Koan:Data:SqliteVec:ConnectionString"] = Memory,
            // Database cell: sqlite-vec does NOT name-fold the routed source; it routes each source to a distinct
            // physical SQLite database via the shared AdapterConnectionResolver. So each conformance source needs a real
            // per-source connection string — a private :memory: database per source is isolated (unshared per connection).
            [$"Koan:Data:Sources:{SourceA}:Adapter"] = "inmemory",
            [$"Koan:Data:Sources:{SourceA}:SqliteVec:ConnectionString"] = Memory,
            [$"Koan:Data:Sources:{SourceB}:Adapter"] = "inmemory",
            [$"Koan:Data:Sources:{SourceB}:SqliteVec:ConnectionString"] = Memory,
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);
        return (host, null);
    }
}
