using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core;
using Koan.Data.Connector.ElasticSearch;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.VectorAdapterSurface.ElasticSearch.Tests;

/// <summary>
/// ARCH-0103 §9.16 — the ElasticSearch cell of the vector AODB conformance ledger, on a LIVE Elasticsearch. Subclasses
/// the shared <see cref="VectorAodbConformanceSpecsBase"/>: the kit's four co-defined cells run against a real
/// <c>AddKoan()</c> host targeting the container. ES stores <c>__koan_tenant</c> as a faithful nested doc field (no
/// overlay rename needed), and <c>RefreshMode=true</c> gives synchronous read-your-writes (no settle poll). Replaces the
/// bespoke <c>ElasticSearchVectorIsolationSpec</c> (the <c>ElasticSearchMatrixSpecs</c> is preserved). Skips when
/// Docker/Elasticsearch is unreachable.
/// </summary>
public sealed class ElasticSearchVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private IContainer? _es;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        IContainer es;
        try
        {
            es = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:8.13.4")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPath("/_cluster/health").ForPort(9200)))
                .Build();
            await es.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (null, $"ElasticSearch/Docker unavailable: {ex.GetType().Name}: {ex.Message}");
        }
        _es = es;

        var endpoint = $"http://localhost:{es.GetMappedPublicPort(9200)}";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:ElasticSearch:Endpoint"] = endpoint,
            ["Koan:Data:ElasticSearch:ConnectionString"] = endpoint,
            ["Koan:Data:ElasticSearch:Dimension"] = "8",
            ["Koan:Data:ElasticSearch:RefreshMode"] = "true",
            ["Koan:Data:ElasticSearch:DisableAutoDetection"] = "true",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);

        // Defense-in-depth: confirm the adapter resolved to THIS Testcontainers Elasticsearch, not a stray local one.
        host.Services.GetRequiredService<IOptions<ElasticSearchOptions>>().Value.Endpoint
            .Should().Be(endpoint, "the adapter must target THIS Testcontainers Elasticsearch, not a stray local one");
        var health = host.Services.GetServices<IHealthContributor>()
            .Single(contributor => contributor.Name == "data:elasticsearch");
        health.IsCritical.Should().BeFalse("an available but unused Vector provider is not a runtime dependency");
        (await health.Check()).State.Should().Be(
            Koan.Core.Observability.Health.HealthState.Unknown,
            "unused provider health must remain connection-free");
        return (host, null);
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        if (_es is not null)
        {
            try { await _es.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }
}
