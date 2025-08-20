using System.Buffers;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using Sora.AI.Contracts.Adapters;
using Sora.AI.Contracts.Models;

namespace Sora.Ai.Provider.Ollama;

internal sealed class OllamaAdapter : IAiAdapter
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;
    public string Id { get; }
    public string Name { get; }
    public string Type => "ollama";

    public OllamaAdapter(string id, string name, HttpClient http, string? defaultModel)
    { Id = id; Name = name; _http = http; _defaultModel = defaultModel ?? string.Empty; }

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
        var body = new
        {
            model,
            prompt,
            stream = false,
            options = MapOptions(request.Options)
        };
        using var resp = await _http.PostAsJsonAsync("/api/generate", body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct).ConfigureAwait(false)
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
        var body = JsonSerializer.Serialize(new { model, prompt, stream = true, options = MapOptions(request.Options) });
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
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
            var body = new { model, input };
            using var resp = await _http.PostAsJsonAsync("/api/embeddings", body, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<OllamaEmbeddingsResponse>(cancellationToken: ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Empty response from Ollama.");
            vectors.Add(doc.embedding ?? Array.Empty<float>());
        }
        return new AiEmbeddingsResponse { Vectors = vectors, Model = model, Dimension = vectors.FirstOrDefault()?.Length };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken: ct).ConfigureAwait(false);
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

    private static object MapOptions(AiPromptOptions? o)
        => o is null ? new { } : new
        {
            temperature = o.Temperature,
            top_p = o.TopP,
            num_predict = o.MaxOutputTokens,
            stop = o.Stop
        };

    private static async IAsyncEnumerable<T?> ReadJsonLinesAsync<T>(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;
            T? obj;
            try { obj = JsonSerializer.Deserialize<T>(line); }
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
