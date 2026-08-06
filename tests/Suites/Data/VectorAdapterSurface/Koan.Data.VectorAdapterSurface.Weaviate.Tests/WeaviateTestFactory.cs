using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>One pinned live Weaviate runtime shared by the provider suite.</summary>
public sealed class WeaviateTestFactory : IAsyncLifetime
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
            _container = new ContainerBuilder("cr.weaviate.io/semitechnologies/weaviate:1.37.6")
                .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
                .WithEnvironment("AUTOSCHEMA_ENABLED", "false")
                .WithEnvironment("ASYNC_INDEXING", "false")
                .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
                .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
                .WithEnvironment("CLUSTER_HOSTNAME", "node1")
                .WithEnvironment("RAFT_BOOTSTRAP_EXPECT", "1")
                .WithPortBinding(port, 8080)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/v1/.well-known/ready")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            Endpoint = $"http://localhost:{_container.GetMappedPublicPort(8080)}";
            _http = Client(Endpoint);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Weaviate/Docker unavailable: {error.GetType().Name}: {error.Message}";
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
        using var listed = await _http.GetAsync("/v1/schema", ct).ConfigureAwait(false);
        listed.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("classes", out var classes)) return;
        foreach (var item in classes.EnumerateArray())
        {
            var name = item.GetProperty("class").GetString()!;
            using var removed = await _http.DeleteAsync($"/v1/schema/{Uri.EscapeDataString(name)}", ct)
                .ConfigureAwait(false);
            removed.EnsureSuccessStatusCode();
        }
    }

    public async Task PutCollection(string name, string description, CancellationToken ct = default)
    {
        if (_http is null) throw new InvalidOperationException(UnavailableReason);
        var body = JsonSerializer.Serialize(new
        {
            @class = name,
            description,
            vectorizer = "none",
            vectorIndexType = "hnsw",
            vectorIndexConfig = new { distance = "cosine" },
            properties = new object[]
            {
                new { name = "koanId", dataType = new[] { "text" }, tokenization = "field" },
                new { name = "koanMetadata", dataType = new[] { "blob" } },
                new { name = "koanTerms", dataType = new[] { "text[]" }, tokenization = "field" }
            }
        });
        using var response = await _http.PostAsync(
            "/v1/schema", new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        var restarted = $"http://localhost:{_container.GetMappedPublicPort(8080)}";
        if (!string.Equals(Endpoint, restarted, StringComparison.Ordinal))
            throw new InvalidOperationException($"Weaviate restart changed endpoint from '{Endpoint}' to '{restarted}'.");
        _http?.Dispose();
        _http = Client(Endpoint);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(30));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync("/v1/.well-known/ready", readiness.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch when (!readiness.IsCancellationRequested) { }
            await Task.Delay(250, readiness.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("Weaviate did not become ready after restart.");
    }

    private static HttpClient Client(string endpoint) => new()
    {
        BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
