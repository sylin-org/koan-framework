using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.ElasticSearch.Tests;

/// <summary>One pinned live Elasticsearch runtime shared by the executable DAC-55 ledger.</summary>
public sealed class ElasticSearchTestFactory : IAsyncLifetime
{
    private IContainer? _container;
    private HttpClient? _http;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            var port = GrabFreePort();
            _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.4.3")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(port, 9200)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(9200).ForPath("/_cluster/health")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            Endpoint = $"http://localhost:{_container.GetMappedPublicPort(9200)}";
            _http = CreateClient(Endpoint);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Elasticsearch/Docker unavailable: {error.GetType().Name}: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_container is not null)
        {
            try { await _container.DisposeAsync().ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task Reset(CancellationToken ct = default)
    {
        if (!IsAvailable || _http is null) return;
        using var listed = await _http.GetAsync("/_cat/indices?format=json&h=index", ct).ConfigureAwait(false);
        listed.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var name = row.GetProperty("index").GetString()!;
            if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            using var removed = await _http.DeleteAsync($"/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
            removed.EnsureSuccessStatusCode();
        }
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        var restarted = $"http://localhost:{_container.GetMappedPublicPort(9200)}";
        if (!string.Equals(Endpoint, restarted, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Elasticsearch restart changed its mapped endpoint from '{Endpoint}' to '{restarted}'.");
        _http?.Dispose();
        _http = CreateClient(Endpoint);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(30));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(
                    "/_cluster/health?wait_for_status=yellow&wait_for_no_relocating_shards=true&timeout=5s",
                    readiness.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch when (!readiness.IsCancellationRequested) { }
            await Task.Delay(250, readiness.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("Elasticsearch did not become ready after restart.");
    }

    private static HttpClient CreateClient(string endpoint) => new()
    {
        BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
