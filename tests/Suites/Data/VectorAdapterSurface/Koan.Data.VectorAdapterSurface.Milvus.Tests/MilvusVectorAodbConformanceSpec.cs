using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Koan.Core;
using Koan.Data.Vector.Connector.Milvus;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

/// <summary>
/// ARCH-0103 §9.16 — the Milvus cell of the vector AODB conformance ledger, on a LIVE Milvus. Subclasses the shared
/// <see cref="VectorAodbConformanceSpecsBase"/>: the kit's four co-defined cells run against a real <c>AddKoan()</c> host
/// targeting a Milvus 2.4 standalone stack (etcd + minio + milvus on a shared network, per its official compose). Milvus
/// stores <c>__koan_tenant</c> as a faithful field and <c>ConsistencyLevel=Strong</c> gives read-your-writes (no settle
/// poll). The empty-<c>CollectionName</c> fix is preserved by leaving <c>CollectionName</c> unset so partition+source
/// folding governs naming. Replaces the bespoke <c>MilvusVectorIsolationSpec</c> (the <c>MilvusMatrixSpecs</c> is
/// preserved). Skips when Docker/Milvus is unreachable.
/// </summary>
public sealed class MilvusVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private INetwork? _network;
    private IContainer? _etcd;
    private IContainer? _minio;
    private IContainer? _milvus;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        try
        {
            // Milvus 2.4 standalone wants three real services on a shared network — etcd + minio + milvus —
            // exactly like its official docker-compose.
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync().ConfigureAwait(false);

            _etcd = new ContainerBuilder("quay.io/coreos/etcd:v3.5.25")
                .WithNetwork(_network)
                .WithNetworkAliases("etcd")
                .WithEnvironment("ETCD_AUTO_COMPACTION_MODE", "revision")
                .WithEnvironment("ETCD_AUTO_COMPACTION_RETENTION", "1000")
                .WithEnvironment("ETCD_QUOTA_BACKEND_BYTES", "4294967296")
                .WithEnvironment("ETCD_SNAPSHOT_COUNT", "50000")
                .WithCommand("etcd",
                    "-advertise-client-urls=http://etcd:2379",
                    "-listen-client-urls=http://0.0.0.0:2379",
                    "--data-dir=/etcd")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready to serve client requests"))
                .Build();
            await _etcd.StartAsync().ConfigureAwait(false);

            _minio = new ContainerBuilder("minio/minio:RELEASE.2024-12-18T13-15-44Z")
                .WithNetwork(_network)
                .WithNetworkAliases("minio")
                .WithEnvironment("MINIO_ACCESS_KEY", "minioadmin")
                .WithEnvironment("MINIO_SECRET_KEY", "minioadmin")
                .WithCommand("minio", "server", "/minio_data")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("API:"))
                .Build();
            await _minio.StartAsync().ConfigureAwait(false);

            _milvus = new ContainerBuilder("milvusdb/milvus:v2.6.20")
                .WithNetwork(_network)
                .WithNetworkAliases("milvus")
                .WithEnvironment("ETCD_ENDPOINTS", "etcd:2379")
                .WithEnvironment("MINIO_ADDRESS", "minio:9000")
                .WithCommand("milvus", "run", "standalone")
                .WithPortBinding(19530, true)
                .WithPortBinding(9091, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Proxy successfully started"))
                .Build();
            await _milvus.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (null, $"Milvus/Docker unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        var endpoint = $"http://localhost:{_milvus.GetMappedPublicPort(19530)}";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Milvus:Endpoint"] = endpoint,
            ["Koan:Data:Milvus:Dimension"] = "8",
            ["Koan:Data:Milvus:Metric"] = "COSINE",
            ["Koan:Data:Milvus:ConsistencyLevel"] = "Strong",
            ["Koan:Data:Milvus:DisableAutoDetection"] = "true",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);

        // Defense-in-depth: confirm the adapter resolved to THIS Testcontainers Milvus, not a stray local one on :19530.
        host.Services.GetRequiredService<IOptions<MilvusOptions>>().Value.Endpoint
            .Should().Be(endpoint, "the adapter must target THIS Testcontainers Milvus, not a stray local one");
        return (host, null);
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        // Tear down in reverse start order: milvus → minio → etcd → network.
        if (_milvus is not null) { try { await _milvus.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_minio is not null) { try { await _minio.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_etcd is not null) { try { await _etcd.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_network is not null) { try { await _network.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }
}
