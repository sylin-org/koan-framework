using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

/// <summary>One pinned live Qdrant runtime shared by the provider suite.</summary>
public sealed class QdrantTestFactory : IAsyncLifetime
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
            var restPort = GrabFreePort();
            _container = new ContainerBuilder("qdrant/qdrant:v1.18.3")
                .WithPortBinding(restPort, 6333)
                .WithPortBinding(6334, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(6333).ForPath("/readyz")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            Endpoint = $"http://localhost:{_container.GetMappedPublicPort(6333)}";
            _http = CreateClient(Endpoint);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Qdrant/Docker unavailable: {error.GetType().Name}: {error.Message}";
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
        using var listed = await _http.GetAsync("/collections", ct).ConfigureAwait(false);
        listed.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        foreach (var collection in document.RootElement.GetProperty("result").GetProperty("collections").EnumerateArray())
        {
            var name = collection.GetProperty("name").GetString()!;
            using var removed = await _http.DeleteAsync($"/collections/{Uri.EscapeDataString(name)}", ct)
                .ConfigureAwait(false);
            removed.EnsureSuccessStatusCode();
        }
    }

    public async Task PutCollection(string name, string json, CancellationToken ct = default)
    {
        if (_http is null) throw new InvalidOperationException(UnavailableReason);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PutAsync($"/collections/{Uri.EscapeDataString(name)}", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        var restartedEndpoint = $"http://localhost:{_container.GetMappedPublicPort(6333)}";
        if (!string.Equals(Endpoint, restartedEndpoint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Qdrant restart changed its mapped endpoint from '{Endpoint}' to '{restartedEndpoint}'.");
        _http?.Dispose();
        _http = CreateClient(Endpoint);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(15));
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync("/readyz", readiness.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch when (!readiness.IsCancellationRequested) { }
            await Task.Delay(100, readiness.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("Qdrant did not become ready after restart.");
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
