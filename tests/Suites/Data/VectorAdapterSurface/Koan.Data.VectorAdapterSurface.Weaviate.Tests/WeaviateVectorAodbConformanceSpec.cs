using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>
/// ARCH-0103 §9.16 — the Weaviate cell of the vector AODB conformance ledger, on a LIVE Weaviate. Subclasses the shared
/// <see cref="VectorAodbConformanceSpecsBase"/>: the kit's four co-defined cells run against a real <c>AddKoan()</c> host
/// targeting the container. Weaviate's GraphQL reserves a leading <c>__</c>, so the adapter declares
/// <c>IOverlayNamingAware("koan_")</c> and the overlay round-trips as <c>koan_tenant</c>; the Shared cell proves that
/// rename isolates a kNN. Replaces the bespoke <c>WeaviateVectorIsolationSpec</c> (the separate
/// <c>WeaviateOverlayIsolationSpec</c> + <c>WeaviateMatrixSpecs</c> are preserved). Skips when Docker/Weaviate is
/// unreachable.
/// </summary>
public sealed class WeaviateVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private IContainer? _weaviate;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        IContainer weaviate;
        try
        {
            weaviate = new ContainerBuilder("semitechnologies/weaviate:1.25.6")
                .WithEnvironment("QUERY_DEFAULTS_LIMIT", "25")
                .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
                .WithEnvironment("AUTOSCHEMA_ENABLED", "true")
                .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
                .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
                .WithEnvironment("CLUSTER_HOSTNAME", "node1")
                .WithEnvironment("RAFT_BOOTSTRAP_EXPECT", "1")
                .WithPortBinding(8080, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPath("/v1/.well-known/ready").ForPort(8080)))
                .Build();
            await weaviate.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (null, $"Weaviate/Docker unavailable: {ex.GetType().Name}: {ex.Message}");
        }
        _weaviate = weaviate;

        var endpoint = $"http://localhost:{weaviate.GetMappedPublicPort(8080)}";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Weaviate:Endpoint"] = endpoint,
            ["Koan:Data:Weaviate:Metric"] = "cosine",
            ["Koan:Data:Weaviate:DisableAutoDetection"] = "true",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);

        // Defense-in-depth: confirm the adapter resolved to THIS Testcontainers Weaviate, not a stray local one on :8080.
        host.Services.GetRequiredService<IOptions<WeaviateOptions>>().Value.Endpoint
            .Should().Be(endpoint, "the adapter must target THIS Testcontainers Weaviate, not a stray local one");
        return (host, null);
    }

    // Weaviate builds its HNSW index asynchronously with no synchronous-refresh knob, so after a save a search can
    // transiently return empty. Poll the entity's own-scope search until the saved id is queryable before asserting,
    // which closes the masking window (a real leak still fails the exact-set assertion once both are indexed).
    protected override async Task SettleAsync<TEntity>(string id, float[] query)
    {
        for (var attempt = 0; attempt < 40; attempt++)   // up to ~10s
        {
            var ids = (await Vector<TEntity>.Search(new VectorQueryOptions(Query: query, TopK: 10))).Matches.Select(m => m.Id);
            if (ids.Contains(id)) return;
            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        if (_weaviate is not null)
        {
            try { await _weaviate.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }
}
