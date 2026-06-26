using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core;
using Koan.Data.Vector.Connector.Qdrant;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

/// <summary>
/// ARCH-0103 §9.16 — the Qdrant cell of the vector AODB conformance ledger, on a LIVE Qdrant (the lightest HTTP vector
/// backend). Subclasses the shared <see cref="VectorAodbConformanceSpecsBase"/>, so the kit's four co-defined cells run
/// against a real <c>AddKoan()</c> host (tenancy + the discoverable <c>VectorConformanceShardAxis</c>) targeting the
/// container: <b>Declares</b> (the decorator declares Container+Database always, RowScoped because Qdrant announces
/// metadata filtering) · <b>Shared</b> (the <c>__koan_tenant</c> payload overlay round-trips through Qdrant and isolates
/// a kNN even when the other tenant's point is nearer) · <b>Container</b> (a distinct partition → a distinct physical
/// collection, proven by-id AND by kNN) · <b>Database</b> (the source-fold → a distinct collection per shard, by-id AND
/// kNN). Replaces the bespoke <c>QdrantVectorIsolationSpec</c> — identical behavioral coverage, now in the shared kit
/// plus the Declares token co-definition. Skips when Docker/Qdrant is unreachable.
/// </summary>
public sealed class QdrantVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private IContainer? _qdrant;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        IContainer qdrant;
        try
        {
            qdrant = new ContainerBuilder("qdrant/qdrant:v1.10.0")
                .WithPortBinding(6333, true)
                .WithPortBinding(6334, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPort(6333).ForPath("/readyz")))
                .Build();
            await qdrant.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (null, $"Qdrant/Docker unavailable: {ex.GetType().Name}: {ex.Message}");
        }
        _qdrant = qdrant;

        var endpoint = $"http://localhost:{qdrant.GetMappedPublicPort(6333)}";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Qdrant:Endpoint"] = endpoint,
            ["Koan:Data:Qdrant:ConnectionString"] = endpoint,
            ["Koan:Data:Qdrant:Dimension"] = "8",
            ["Koan:Data:Qdrant:WaitForResult"] = "true",          // read-your-writes: deterministic test reads
            ["Koan:Data:Qdrant:DisableAutoDetection"] = "true",   // never let discovery find a stray local Qdrant
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // Pin the endpoint deterministically AFTER the connector's configurator (PostConfigure runs last) so a stray
            // local Qdrant (e.g. a dev container on :6333) can never be discovered/used instead of THIS container.
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.PostConfigure<QdrantOptions>(o =>
                {
                    o.ConnectionString = endpoint;
                    o.Endpoint = endpoint;
                    o.Dimension = 8;
                    o.WaitForResult = true;
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        // Defense-in-depth: confirm the adapter resolved to THIS Testcontainers Qdrant, not a stray local one on :6333
        // (the PostConfigure pin + DisableAutoDetection enforce it; this verifies the pin actually held).
        host.Services.GetRequiredService<IOptions<QdrantOptions>>().Value.Endpoint
            .Should().Be(endpoint, "the adapter must target THIS Testcontainers Qdrant, not a stray local one");
        return (host, null);
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        if (_qdrant is not null)
        {
            try { await _qdrant.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }
}
