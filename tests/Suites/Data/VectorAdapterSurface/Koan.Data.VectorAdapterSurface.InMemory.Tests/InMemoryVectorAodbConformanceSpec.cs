using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

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
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);
        return (host, null);
    }
}
