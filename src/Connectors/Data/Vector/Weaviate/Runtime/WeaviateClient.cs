using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed class WeaviateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _maxResponseBytes;
    private int _disposed;

    internal WeaviateClient(IHttpClientFactory factory, WeaviateRoute route, WeaviateOptions options)
    {
        _http = factory.CreateClient(Infrastructure.Constants.HttpClientName);
        _http.BaseAddress = route.Endpoint;
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (route.ApiKey is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", route.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _maxResponseBytes = options.MaxResponseBytes;
    }

    internal Task<WeaviateResponse> Get(string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Get, path, null, operation, allowNotFound, false, ct);

    internal Task<WeaviateResponse> Post(
        string path, byte[] body, string operation, bool allowConflict, CancellationToken ct) =>
        Send(HttpMethod.Post, path, body, operation, false, allowConflict, ct);

    internal Task<WeaviateResponse> Put(string path, byte[] body, string operation, CancellationToken ct) =>
        Send(HttpMethod.Put, path, body, operation, false, false, ct);

    internal Task<WeaviateResponse> Delete(string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Delete, path, null, operation, allowNotFound, false, ct);

    internal async Task Probe(CancellationToken ct)
    {
        using var response = await Get(Infrastructure.Constants.ReadyPath, "readiness probe", false, ct)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
    }

    private async Task<WeaviateResponse> Send(
        HttpMethod method,
        string path,
        byte[]? body,
        string operation,
        bool allowNotFound,
        bool allowConflict,
        CancellationToken ct)
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
            var conflict = response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
            var accepted = response.IsSuccessStatusCode ||
                (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) ||
                (allowConflict && conflict);
            if (!accepted && attempt < Infrastructure.Constants.Defaults.MaxAttempts && IsTransient(response.StatusCode))
            {
                await Task.Delay(Infrastructure.Constants.Defaults.RetryDelayMilliseconds * attempt, ct)
                    .ConfigureAwait(false);
                continue;
            }
            if (!accepted)
                throw new HttpRequestException(
                    $"Weaviate {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentLength == 0)
                return new WeaviateResponse(response.StatusCode, null);
            await response.Content.LoadIntoBufferAsync(_maxResponseBytes, ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            if (stream.Length == 0) return new WeaviateResponse(response.StatusCode, null);
            return new WeaviateResponse(response.StatusCode,
                await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false));
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

internal sealed record WeaviateResponse(HttpStatusCode StatusCode, JsonDocument? Document) : IDisposable
{
    internal bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    internal bool IsConflict => StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
    public void Dispose() => Document?.Dispose();
}
