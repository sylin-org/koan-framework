using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Koan.Data.Vector.Connector.Milvus;

internal sealed class MilvusClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _database;
    private readonly int _maxResponseBytes;
    private int _disposed;

    internal MilvusClient(IHttpClientFactory factory, MilvusRoute route, MilvusOptions options)
    {
        _http = factory.CreateClient(Infrastructure.Constants.HttpClientName);
        _http.BaseAddress = route.Endpoint;
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (route.Token is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", route.Token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _maxResponseBytes = options.MaxResponseBytes;
        _database = route.Database;
    }

    internal Task<MilvusResponse> Post(string path, byte[] body, string operation, bool allowMissing, CancellationToken ct) =>
        Send(path, body, operation, allowMissing, ct);

    internal async Task Probe(CancellationToken ct)
    {
        using var response = await Send(
            "v2/vectordb/collections/list",
            JsonSerializer.SerializeToUtf8Bytes(new { dbName = _database }),
            "readiness probe",
            false,
            ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
    }

    private async Task<MilvusResponse> Send(
        string path,
        byte[] body,
        string operation,
        bool allowMissing,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'));
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && attempt < Infrastructure.Constants.Defaults.MaxAttempts &&
                IsTransient(response.StatusCode))
            {
                await Task.Delay(Infrastructure.Constants.Defaults.RetryDelayMilliseconds * attempt, ct)
                    .ConfigureAwait(false);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Milvus {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);
            await response.Content.LoadIntoBufferAsync(_maxResponseBytes, ct).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            if (stream.Length == 0) throw new InvalidOperationException($"Milvus {operation} returned no JSON result.");
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var value) && value.TryGetInt64(out var parsed) ? parsed : 0L;
            if (code == 0 || code == 200) return new MilvusResponse(document, false);
            if (allowMissing && IsMissing(code, root)) return new MilvusResponse(document, true);
            document.Dispose();
            throw new MilvusRejectedException(operation, code,
                $"Milvus {operation} was rejected with provider code {code}.");
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static bool IsMissing(long code, JsonElement root)
    {
        if (code is 100 or 1001 or 1100) return true;
        if (!root.TryGetProperty("message", out var message)) return false;
        var text = message.GetString();
        return text is not null &&
            (text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("not exist", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record MilvusResponse(JsonDocument Document, bool IsMissing) : IDisposable
{
    internal JsonElement Data => Document.RootElement.TryGetProperty("data", out var data) ? data : default;
    public void Dispose() => Document.Dispose();
}

/// <summary>A Milvus provider rejection carrying the wire error code, so callers can distinguish
/// transient provider states (e.g. 1804 collection-not-loaded right after load) from hard failures.</summary>
internal sealed class MilvusRejectedException(string operation, long code, string message)
    : InvalidOperationException(message)
{
    public string Operation { get; } = operation;
    public long Code { get; } = code;
}
