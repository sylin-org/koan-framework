using System.Net.Http.Headers;
using System.Net.Http.Json;
using Koan.Data.Connector.CouchDb.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.CouchDb.Runtime;

/// <summary>
/// One HTTP client per server endpoint, owned by the host and released with it. CouchDB speaks plain
/// JSON over HTTP, so this is the whole driver: authentication, the document and Mango endpoints, and
/// status-code translation. Transport failures surface as-is; CouchDB's own error payloads surface as
/// <see cref="CouchDbException"/> carrying the store's error and reason verbatim.
/// </summary>
internal sealed class CouchDbClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None
    });

    public CouchDbClient(string endpoint, string? userId, string? password)
    {
        var uri = NormalizeEndpoint(endpoint);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 64
        };
        _http = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(userId))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{userId}:{password}")));
    }

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var value = endpoint.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "http://localhost:5984" : endpoint;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                "The CouchDB endpoint must be an http(s) URL, e.g. http://localhost:5984.", nameof(endpoint));
        return uri;
    }

    // ---- server ----

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("_up", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // ---- databases ----

    public async Task<bool> DatabaseExistsAsync(string database, CancellationToken ct)
    {
        using var response = await _http.GetAsync(Encode(database), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        return response.StatusCode == System.Net.HttpStatusCode.OK;
    }

    public async Task CreateDatabaseAsync(string database, CancellationToken ct)
    {
        using var response = await _http.PutAsync(Encode(database), content: null, ct).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Created && response.StatusCode != System.Net.HttpStatusCode.PreconditionFailed)
            throw await Error(response, $"creating database '{database}'", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("_all_dbs", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<string[]>(response, ct).ConfigureAwait(false) ?? [];
    }

    // ---- documents ----

    public async Task<(bool Found, JObject? Document)> GetDocumentAsync(string database, string id, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"{Encode(database)}/{Encode(id)}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (false, null);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"reading '{database}/{id}'", ct).ConfigureAwait(false);
        return (true, await ReadAsync<JObject>(response, ct).ConfigureAwait(false));
    }

    /// <summary>PUT with a revision: an update race surfaces as 409, which the caller classifies.</summary>
    public async Task<(string Id, string Rev)> PutDocumentAsync(
        string database, string id, JObject document, string? rev, CancellationToken ct)
    {
        document[Constants.Storage.Identity] = id;
        if (rev is not null) document[Constants.Storage.Rev] = rev;
        using var response = await _http.PutAsync(
            $"{Encode(database)}/{Encode(id)}", Json(document), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"writing '{database}/{id}'", ct).ConfigureAwait(false);
        var payload = await ReadAsync<JObject>(response, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"CouchDB returned no body for the write of '{database}/{id}'.");
        return (payload.Value<string>("id") ?? id, payload.Value<string>("rev")
            ?? throw new InvalidOperationException($"CouchDB returned no revision for the write of '{database}/{id}'."));
    }

    public async Task<bool> DeleteDocumentAsync(string database, string id, string rev, CancellationToken ct)
    {
        using var response = await _http.DeleteAsync($"{Encode(database)}/{Encode(id)}?rev={Uri.EscapeDataString(rev)}", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"deleting '{database}/{id}'", ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The keyed batch read: rows come back in key order with a null document per missing key.
    /// </summary>
    public async Task<(string Id, JObject? Doc, string? Rev)[]> GetDocumentsAsync(
        string database, IReadOnlyList<string> keys, CancellationToken ct)
    {
        using var response = await _http.PostAsync(
            $"{Encode(database)}/_all_docs?include_docs=true",
            Json(new JObject { ["keys"] = new JArray(keys) }), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"batch-reading '{database}'", ct).ConfigureAwait(false);
        var payload = await ReadAsync<JObject>(response, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"CouchDB returned no body for the batch read of '{database}'.");
        return payload.Value<JArray>("rows")!.OfType<JObject>()
            .Select(row => (
                row.Value<string>("id") ?? row.Value<string>("key") ?? string.Empty,
                row.Value<JObject>("doc"),
                row.SelectToken("value.rev")?.Value<string>()))
            .ToArray();
    }

    public async Task<IReadOnlyList<(string Id, string Rev)>> AllRevisionsAsync(string database, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"{Encode(database)}/_all_docs", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"listing '{database}'", ct).ConfigureAwait(false);
        var payload = await ReadAsync<JObject>(response, ct).ConfigureAwait(false);
        return payload?.Value<JArray>("rows")!.OfType<JObject>()
            .Select(row => (row.Value<string>("id") ?? string.Empty, row.SelectToken("value.rev")?.Value<string>() ?? string.Empty))
            .ToArray() ?? [];
    }

    /// <summary>Per-document writes; CouchDB commits each independently, so this is not an atomic batch.</summary>
    public async Task<IReadOnlyList<JObject>> BulkDocsAsync(string database, IReadOnlyList<JObject> docs, CancellationToken ct)
    {
        using var response = await _http.PostAsync(
            $"{Encode(database)}/_bulk_docs", Json(new JObject { ["docs"] = new JArray(docs) }), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"bulk-writing '{database}'", ct).ConfigureAwait(false);
        var payload = await ReadAsync<JArray>(response, ct).ConfigureAwait(false);
        return payload?.OfType<JObject>().ToArray() ?? [];
    }

    /// <summary>Mango find; the caller applies limit and skip only when the page is provider-handled.</summary>
    public async Task<IReadOnlyList<JObject>> FindAsync(
        string database, JObject selector, IReadOnlyList<JObject>? sort, int? limit, int? skip, IReadOnlyList<string>? fields, CancellationToken ct)
    {
        var request = new JObject { ["selector"] = selector };
        if (sort is { Count: > 0 }) request["sort"] = new JArray(sort);
        if (limit is { } l) request["limit"] = l;
        if (skip is { } s) request["skip"] = s;
        if (fields is not null) request["fields"] = new JArray(fields);
        using var response = await _http.PostAsync(
            $"{Encode(database)}/_find", Json(request), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, $"finding in '{database}'", ct).ConfigureAwait(false);
        var payload = await ReadAsync<JObject>(response, ct).ConfigureAwait(false);
        return payload?.Value<JArray>("docs")!.OfType<JObject>().ToArray() ?? [];
    }

    // ---- plumbing ----

    private StringContent Json(JToken token) =>
        new(token.ToString(Formatting.None), System.Text.Encoding.UTF8, "application/json");

    private static string Encode(string segment) => Uri.EscapeDataString(segment);

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new JsonTextReader(new StreamReader(stream)) { DateParseHandling = DateParseHandling.None };
        return _serializer.Deserialize<T>(reader);
    }

    private static async Task<CouchDbException> Error(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        string error = response.StatusCode.ToString();
        string reason = string.Empty;
        try
        {
            var payload = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            error = payload?.Value<string>("error") ?? error;
            reason = payload?.Value<string>("reason") ?? string.Empty;
        }
        catch { /* a non-JSON error body still carries the status code */ }
        return new CouchDbException(operation, (int)response.StatusCode, error, reason);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>The store's own error/reason pair, verbatim, with the status code and the operation that failed.</summary>
internal sealed class CouchDbException(string operation, int status, string error, string reason)
    : InvalidOperationException($"CouchDB failed {operation}: HTTP {status} {error}{(string.IsNullOrEmpty(reason) ? string.Empty : $" — {reason}")}.")
{
    public string Operation { get; } = operation;
    public int Status { get; } = status;
    public string Error { get; } = error;
    public string Reason { get; } = reason;
}
