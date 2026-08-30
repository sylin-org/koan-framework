using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Chroma.Tests;

/// <summary>One pinned live Chroma runtime shared by the provider suite. The image ships no
/// HEALTHCHECK, so readiness waits on the v2 heartbeat endpoint rather than the container strategy.</summary>
public sealed class ChromaTestFactory : IAsyncLifetime
{
    private const string TenantPath = "api/v2/tenants/default_tenant/databases/default_database";

    private IContainer? _container;
    private HttpClient? _http;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            var restPort = GrabFreePort();
            _container = new ContainerBuilder("chromadb/chroma:1.5.9")
                .WithPortBinding(restPort, 8000)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(8000).ForPath("/api/v2/heartbeat")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            Endpoint = $"http://localhost:{_container.GetMappedPublicPort(8000)}";
            _http = CreateClient(Endpoint);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Chroma/Docker unavailable: {error.GetType().Name}: {error.Message}";
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
        using var listed = await _http.GetAsync(TenantPath + "/collections", ct).ConfigureAwait(false);
        listed.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        foreach (var collection in document.RootElement.EnumerateArray())
        {
            var name = collection.GetProperty("name").GetString()!;
            using var removed = await _http.DeleteAsync($"{TenantPath}/collections/{Uri.EscapeDataString(name)}", ct)
                .ConfigureAwait(false);
            removed.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Pre-provisions a collection whose dimension Chroma pins from a first write, so shape
    /// validation can be proven against a wrong-dimensional store (V-01).</summary>
    public async Task PutWrongShapeCollection(string name, int dimension, CancellationToken ct = default)
    {
        if (_http is null) throw new InvalidOperationException(UnavailableReason);
        var create = """
            {"name":%NAME%,"get_or_create":false,"configuration":{"hnsw":{"space":"cosine"}}}
            """.Replace("%NAME%", JsonSerializer.Serialize(name), StringComparison.Ordinal);
        using (var content = new StringContent(create, Encoding.UTF8, "application/json"))
        using (var created = await _http.PostAsync($"{TenantPath}/collections", content, ct).ConfigureAwait(false))
        {
            created.EnsureSuccessStatusCode();
        }
        var id = await CollectionId(name, ct).ConfigureAwait(false);
        var embedding = '[' + string.Join(',', Enumerable.Repeat("0.5", dimension)) + ']';
        var upsert = """
            {"ids":["wrong"],"embeddings":[%EMBEDDING%],"metadatas":[{}],"documents":null}
            """.Replace("%EMBEDDING%", embedding, StringComparison.Ordinal);
        using (var content = new StringContent(upsert, Encoding.UTF8, "application/json"))
        using (var written = await _http.PostAsync($"{TenantPath}/collections/{id}/upsert", content, ct).ConfigureAwait(false))
        {
            written.EnsureSuccessStatusCode();
        }
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        var restartedEndpoint = $"http://localhost:{_container.GetMappedPublicPort(8000)}";
        if (!string.Equals(Endpoint, restartedEndpoint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Chroma restart changed its mapped endpoint from '{Endpoint}' to '{restartedEndpoint}'.");
        _http?.Dispose();
        _http = CreateClient(Endpoint);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(15));
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync("/api/v2/heartbeat", readiness.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch when (!readiness.IsCancellationRequested) { }
            await Task.Delay(100, readiness.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("Chroma did not become ready after restart.");
    }

    private async Task<string> CollectionId(string name, CancellationToken ct)
    {
        using var got = await _http!.GetAsync($"{TenantPath}/collections/{Uri.EscapeDataString(name)}", ct)
            .ConfigureAwait(false);
        if (got.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Chroma collection '{name}' was not provisioned.");
        got.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await got.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"Chroma collection '{name}' returned no id.");
    }

    private static HttpClient CreateClient(string endpoint) => new()
    {
        BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
