using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Koan.Data.Vector.Connector.Qdrant;

internal sealed class QdrantClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _maxResponseBytes;
    private int _disposed;

    internal QdrantClient(IHttpClientFactory factory, QdrantRoute route, QdrantOptions options)
    {
        _http = factory.CreateClient(Infrastructure.Constants.HttpClientName);
        _http.BaseAddress = route.Endpoint;
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (route.ApiKey is not null)
            _http.DefaultRequestHeaders.TryAddWithoutValidation("api-key", route.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _maxResponseBytes = options.MaxResponseBytes;
    }

    internal Task<QdrantResponse> Get(string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Get, path, null, operation, allowNotFound, ct);

    internal Task<QdrantResponse> Put(string path, byte[] body, string operation, bool allowConflict, CancellationToken ct) =>
        Send(HttpMethod.Put, path, body, operation, false, ct, allowConflict);

    internal Task<QdrantResponse> Post(string path, byte[] body, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Post, path, body, operation, allowNotFound, ct);

    internal async Task Probe(CancellationToken ct)
    {
        using var response = await Get(Infrastructure.Constants.ReadyPath, "readiness probe", false, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
    }

    private async Task<QdrantResponse> Send(
        HttpMethod method,
        string path,
        byte[]? body,
        string operation,
        bool allowNotFound,
        CancellationToken ct,
        bool allowConflict = false)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var accepted = response.IsSuccessStatusCode ||
                (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) ||
                (allowConflict && response.StatusCode == HttpStatusCode.Conflict);
            if (!accepted && attempt < Infrastructure.Constants.Defaults.MaxAttempts && IsTransient(response.StatusCode))
            {
                await Task.Delay(Infrastructure.Constants.Defaults.RetryDelayMilliseconds * attempt, ct)
                    .ConfigureAwait(false);
                continue;
            }
            if (!accepted)
                throw new HttpRequestException(
                    $"Qdrant {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentLength == 0)
                return new QdrantResponse(response.StatusCode, null);
            await response.Content.LoadIntoBufferAsync(_maxResponseBytes, ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            if (stream.Length == 0) return new QdrantResponse(response.StatusCode, null);
            return new QdrantResponse(response.StatusCode, await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false));
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

internal sealed record QdrantResponse(HttpStatusCode StatusCode, JsonDocument? Document) : IDisposable
{
    internal bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public void Dispose() => Document?.Dispose();
}
