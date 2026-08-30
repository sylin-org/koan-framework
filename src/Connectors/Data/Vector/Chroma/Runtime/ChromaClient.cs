using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Koan.Data.Vector.Connector.Chroma;

internal sealed class ChromaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ChromaRoute _route;
    private readonly int _maxResponseBytes;
    private int _disposed;

    internal ChromaClient(IHttpClientFactory factory, ChromaRoute route, ChromaOptions options)
    {
        _http = factory.CreateClient(Infrastructure.Constants.HttpClientName);
        _http.BaseAddress = route.Endpoint;
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (route.ApiKey is not null)
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {route.ApiKey}");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _route = route;
        _maxResponseBytes = options.MaxResponseBytes;
    }

    internal string BasePath =>
        $"{Infrastructure.Constants.ApiBase}/tenants/{Escape(_route.Tenant)}/databases/{Escape(_route.Database)}";

    internal Task<ChromaResponse> Get(string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Get, path, null, operation, allowNotFound, ct);

    internal Task<ChromaResponse> Post(string path, byte[] body, string operation, bool allowNotFound, CancellationToken ct, bool allowConflict = false) =>
        Send(HttpMethod.Post, path, body, operation, allowNotFound, ct, allowConflict);

    internal Task<ChromaResponse> Delete(string path, byte[]? body, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Delete, path, body, operation, allowNotFound, ct);

    internal async Task Probe(CancellationToken ct)
    {
        using var response = await Get(
            Infrastructure.Constants.HeartbeatPath, "readiness probe", false, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
    }

    private async Task<ChromaResponse> Send(
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
                    $"Chroma {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentLength == 0)
                return new ChromaResponse(response.StatusCode, null);
            await response.Content.LoadIntoBufferAsync(_maxResponseBytes, ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            if (stream.Length == 0) return new ChromaResponse(response.StatusCode, null);
            return new ChromaResponse(response.StatusCode, await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false));
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    internal static string Escape(string value) => Uri.EscapeDataString(value);
}

internal sealed record ChromaResponse(HttpStatusCode StatusCode, JsonDocument? Document) : IDisposable
{
    internal bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    internal bool IsConflict => StatusCode == HttpStatusCode.Conflict;
    public void Dispose() => Document?.Dispose();
}
