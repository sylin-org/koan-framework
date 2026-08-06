using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Koan.Data.Connector.ElasticSearch;

internal sealed class ElasticSearchClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _maxResponseBytes;
    private int _disposed;

    internal ElasticSearchClient(IHttpClientFactory factory, ElasticSearchRoute route, ElasticSearchOptions options)
    {
        _http = factory.CreateClient(Infrastructure.Constants.HttpClientName);
        _http.BaseAddress = route.Endpoint;
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (route.ApiKey is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", route.ApiKey);
        else if (route.Username is not null)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{route.Username}:{route.Password ?? string.Empty}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _maxResponseBytes = options.MaxResponseBytes;
    }

    internal Task<ElasticSearchResponse> Get(string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Get, path, null, null, operation, readOnly: true, allowNotFound, allowBadRequest: false, ct);

    internal Task<ElasticSearchResponse> Put(
        string path, byte[] body, string operation, bool allowBadRequest, CancellationToken ct) =>
        Send(HttpMethod.Put, path, body, "application/json", operation, readOnly: false,
            allowNotFound: false, allowBadRequest, ct);

    internal Task<ElasticSearchResponse> Post(
        string path, byte[] body, string operation, bool readOnly, bool allowNotFound, string mediaType,
        CancellationToken ct) =>
        Send(HttpMethod.Post, path, body, mediaType, operation, readOnly, allowNotFound,
            allowBadRequest: false, ct);

    internal Task<ElasticSearchResponse> Delete(
        string path, string operation, bool allowNotFound, CancellationToken ct) =>
        Send(HttpMethod.Delete, path, null, null, operation, readOnly: false, allowNotFound,
            allowBadRequest: false, ct);

    internal async Task Probe(CancellationToken ct)
    {
        using var response = await Get(Infrastructure.Constants.HealthPath, "readiness probe", false, ct)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
    }

    private async Task<ElasticSearchResponse> Send(
        HttpMethod method,
        string path,
        byte[]? body,
        string? mediaType,
        string operation,
        bool readOnly,
        bool allowNotFound,
        bool allowBadRequest,
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
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType!);
            }
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var accepted = response.IsSuccessStatusCode ||
                (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) ||
                (allowBadRequest && response.StatusCode == HttpStatusCode.BadRequest);
            if (!accepted && readOnly && attempt < Infrastructure.Constants.Defaults.MaxAttempts &&
                IsTransient(response.StatusCode))
            {
                await Task.Delay(Infrastructure.Constants.Defaults.RetryDelayMilliseconds * attempt, ct)
                    .ConfigureAwait(false);
                continue;
            }
            if (!accepted)
                throw new HttpRequestException(
                    $"Elasticsearch {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null,
                    response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentLength == 0)
                return new ElasticSearchResponse(response.StatusCode, null);
            await response.Content.LoadIntoBufferAsync(_maxResponseBytes, ct).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return new ElasticSearchResponse(response.StatusCode, null);
            return new ElasticSearchResponse(response.StatusCode, JsonDocument.Parse(bytes));
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

internal sealed record ElasticSearchResponse(HttpStatusCode StatusCode, JsonDocument? Document) : IDisposable
{
    internal bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    internal bool IsBadRequest => StatusCode == HttpStatusCode.BadRequest;
    public void Dispose() => Document?.Dispose();
}
