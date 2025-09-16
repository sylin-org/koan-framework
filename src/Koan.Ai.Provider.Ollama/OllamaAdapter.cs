using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using System.Text;

namespace Koan.Ai.Provider.Ollama;

internal sealed class OllamaAdapter : IAiAdapter
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;
    private readonly ILogger<OllamaAdapter>? _logger;
    public string Id { get; }
    public string Name { get; }
    public string Type => Infrastructure.Constants.Adapter.Type;

    public OllamaAdapter(string id, string name, HttpClient http, string? defaultModel, ILogger<OllamaAdapter>? logger = null)
    { Id = id; Name = name; _http = http; _defaultModel = defaultModel ?? string.Empty; _logger = logger; }

    public bool CanServe(AiChatRequest request)
    {
        // Accept if no model specified or model looks like an ollama tag; be permissive by default.
        return true;
    }

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        var prompt = BuildPrompt(request);
        var bodyObj = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["options"] = MapOptions(request.Options)
        };
        if (request.Options?.Think is bool thinkFlag)
            bodyObj["think"] = thinkFlag;
        var body = bodyObj;
        _logger?.LogDebug("Ollama: POST {Path} model={Model}", Infrastructure.Constants.Api.GeneratePath, model);
    var payload = JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    using var resp = await _http.PostAsync(Infrastructure.Constants.Api.GeneratePath, new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger?.LogWarning("Ollama: generate failed ({Status}) body={Body}", (int)resp.StatusCode, text);
        }
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    var doc = JsonConvert.DeserializeObject<OllamaGenerateResponse>(json)
          ?? throw new InvalidOperationException("Empty response from Ollama.");
        return new AiChatResponse
        {
            Text = doc.response ?? string.Empty,
            FinishReason = doc.done_reason,
            Model = doc.model
        };
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        var prompt = BuildPrompt(request);
    var streamBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["options"] = MapOptions(request.Options)
        };
        if (request.Options?.Think is bool thinkFlag2)
            streamBody["think"] = thinkFlag2;
    var body = JsonConvert.SerializeObject(streamBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, Infrastructure.Constants.Api.GeneratePath)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        _logger?.LogDebug("Ollama: STREAM {Path} model={Model}", Infrastructure.Constants.Api.GeneratePath, model);
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await foreach (var part in ReadJsonLinesAsync<OllamaGenerateResponse>(resp, ct))
        {
            if (part is null) continue;
            if (!string.IsNullOrEmpty(part.response))
            {
                yield return new AiChatChunk { DeltaText = part.response };
            }
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");

        var vectors = new List<float[]>();
        foreach (var input in request.Input)
        {
            // Ollama embeddings API expects 'prompt' as the input field
            var body = new { model, prompt = input };
            var payload = JsonConvert.SerializeObject(body);
            using var resp = await _http.PostAsync(Infrastructure.Constants.Api.EmbeddingsPath, new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger?.LogWarning("Ollama: embeddings failed ({Status}) body={Body}", (int)resp.StatusCode, text);
            }
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonConvert.DeserializeObject<OllamaEmbeddingsResponse>(json)
                      ?? throw new InvalidOperationException("Empty response from Ollama.");
            vectors.Add(doc.embedding ?? Array.Empty<float>());
        }
        var dim = vectors.FirstOrDefault()?.Length ?? 0;
        _logger?.LogDebug("Ollama: embeddings ok model={Model} dim={Dim} count={Count}", model, dim, vectors.Count);
        return new AiEmbeddingsResponse { Vectors = vectors, Model = model, Dimension = dim };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
    {
    using var resp = await _http.GetAsync(Infrastructure.Constants.Discovery.TagsPath, ct).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    var doc = JsonConvert.DeserializeObject<OllamaTagsResponse>(json);
        var models = new List<AiModelDescriptor>();
        foreach (var m in doc?.models ?? Enumerable.Empty<OllamaTag>())
        {
            models.Add(new AiModelDescriptor
            {
                Name = m.name ?? string.Empty,
                Family = m.model,
                AdapterId = Id,
                AdapterType = Type
            });
        }
        return models;
    }

    public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(new AiCapabilities
        {
            AdapterId = Id,
            AdapterType = Type,
            SupportsChat = true,
            SupportsStreaming = true,
            SupportsEmbeddings = true
        });

    private static string BuildPrompt(AiChatRequest req)
    {
        if (req.Messages.Count == 1 && string.Equals(req.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
            return req.Messages[0].Content;
        var sb = new StringBuilder();
        foreach (var m in req.Messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[system]\n{m.Content}\n");
            else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[user]\n{m.Content}\n");
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[assistant]\n{m.Content}\n");
        }
        return sb.ToString();
    }

    private static IDictionary<string, object?> MapOptions(AiPromptOptions? o)
    {
        if (o is null) return new Dictionary<string, object?>();
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["temperature"] = o.Temperature,
            ["top_p"] = o.TopP,
            ["num_predict"] = o.MaxOutputTokens,
            ["stop"] = o.Stop,
            ["seed"] = o.Seed
        };
        if (o.VendorOptions is { Count: > 0 })
        {
            foreach (var kv in o.VendorOptions)
            {
                var normKey = NormalizeOllamaOptionKey(kv.Key);
                // Promote JsonElement to raw boxed value when possible
                dict[normKey] = kv.Value.Type switch
                {
                    JTokenType.String => kv.Value.Value<string>(),
                    JTokenType.Integer => kv.Value.Value<long>(),
                    JTokenType.Float => kv.Value.Value<double>(),
                    JTokenType.Boolean => kv.Value.Value<bool>(),
                    JTokenType.Array => kv.Value, // pass-through
                    JTokenType.Object => kv.Value,
                    _ => null
                };
            }
        }
        return dict;
    }

    private static object? TryGetNumber(JToken tok)
        => tok.Type == JTokenType.Integer ? tok.Value<long>() : (tok.Type == JTokenType.Float ? tok.Value<double>() : null);

    private static string NormalizeOllamaOptionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var k = key.Trim();
        // Normalize common synonyms and casing to Ollama's expected names
        if (k.Equals("temp", StringComparison.OrdinalIgnoreCase) || k.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            return "temperature";
        if (k.Equals("top_p", StringComparison.OrdinalIgnoreCase) || k.Equals("topp", StringComparison.OrdinalIgnoreCase) || k.Equals("topP", StringComparison.Ordinal))
            return "top_p";
        if (k.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("max_new_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("maxOutputTokens", StringComparison.Ordinal) || k.Equals("max_output_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("num_predict", StringComparison.OrdinalIgnoreCase))
            return "num_predict";
        if (k.Equals("stop_sequences", StringComparison.OrdinalIgnoreCase) || k.Equals("stopSequences", StringComparison.Ordinal) || k.Equals("stops", StringComparison.OrdinalIgnoreCase) || k.Equals("stop", StringComparison.OrdinalIgnoreCase))
            return "stop";
        if (k.Equals("seed", StringComparison.OrdinalIgnoreCase))
            return "seed";
        return k; // passthrough as-is
    }

    private static async IAsyncEnumerable<T?> ReadJsonLinesAsync<T>(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;
            T? obj;
            try { obj = JsonConvert.DeserializeObject<T>(line); }
            catch { continue; }
            yield return obj;
        }
    }

    private sealed class OllamaGenerateResponse
    {
        public string? model { get; set; }
        public string? response { get; set; }
        public bool done { get; set; }
        public string? done_reason { get; set; }
    }

    private sealed class OllamaEmbeddingsResponse
    {
        public float[]? embedding { get; set; }
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaTag> models { get; set; } = new();
    }
    private sealed class OllamaTag
    {
        public string? name { get; set; }
        public string? model { get; set; }
    }
}
