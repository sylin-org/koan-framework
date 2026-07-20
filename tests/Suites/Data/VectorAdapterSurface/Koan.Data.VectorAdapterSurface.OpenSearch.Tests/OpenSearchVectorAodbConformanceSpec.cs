using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core;
using Koan.Data.Connector.OpenSearch;
using Koan.Data.VectorAdapterSurface.TestKit;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.VectorAdapterSurface.OpenSearch.Tests;

/// <summary>
/// ARCH-0103 §9.16 — the OpenSearch cell of the vector AODB conformance ledger, on a LIVE OpenSearch. Subclasses the
/// shared <see cref="VectorAodbConformanceSpecsBase"/>: the kit's four co-defined cells run against a real
/// <c>AddKoan()</c> host targeting the container. OpenSearch stores <c>__koan_tenant</c> as a faithful nested doc field,
/// and <c>RefreshMode=true</c> gives synchronous read-your-writes (no settle poll). Replaces the bespoke
/// <c>OpenSearchVectorIsolationSpec</c> (the <c>OpenSearchMatrixSpecs</c> is preserved). Skips when Docker/OpenSearch is
/// unreachable.
/// </summary>
public sealed class OpenSearchVectorAodbConformanceSpec : VectorAodbConformanceSpecsBase
{
    private IContainer? _opensearch;

    protected override async Task<(IntegrationHost? host, string? skip)> BootHostAsync()
    {
        IContainer opensearch;
        try
        {
            opensearch = new ContainerBuilder("opensearchproject/opensearch:3.7.0")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
                .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPath("/_cluster/health").ForPort(9200)))
                .Build();
            await opensearch.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (null, $"OpenSearch/Docker unavailable: {ex.GetType().Name}: {ex.Message}");
        }
        _opensearch = opensearch;

        var endpoint = $"http://localhost:{opensearch.GetMappedPublicPort(9200)}";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Tenancy:Posture"] = "Closed",
            ["Koan:Data:OpenSearch:Endpoint"] = endpoint,
            ["Koan:Data:OpenSearch:ConnectionString"] = endpoint,
            ["Koan:Data:OpenSearch:Dimension"] = "8",
            ["Koan:Data:OpenSearch:RefreshMode"] = "true",
            ["Koan:Data:OpenSearch:DisableAutoDetection"] = "true",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);

        // Defense-in-depth: confirm the adapter resolved to THIS Testcontainers OpenSearch, not a stray local one.
        host.Services.GetRequiredService<IOptions<OpenSearchOptions>>().Value.Endpoint
            .Should().Be(endpoint, "the adapter must target THIS Testcontainers OpenSearch, not a stray local one");
        var health = host.Services.GetServices<IHealthContributor>()
            .Single(contributor => contributor.Name == "data:opensearch");
        health.IsCritical.Should().BeFalse("an available but unused Vector provider is not a runtime dependency");
        (await health.Check()).State.Should().Be(
            Koan.Core.Observability.Health.HealthState.Unknown,
            "unused provider health must remain connection-free");
        return (host, null);
    }

    protected override async ValueTask DisposeBackendAsync()
    {
        if (_opensearch is not null)
        {
            try { await _opensearch.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }
}
