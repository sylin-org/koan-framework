using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Connector.ZenGarden;

/// <summary>
/// Unified AI adapter that routes ALL capabilities through the Zen Garden AI Orchestrator.
/// Implements every Protocol adapter interface — the orchestrator handles native API dispatch
/// to individual services (Ollama, ComfyUI, whisper.cpp, Infinity, etc.).
/// </summary>
[AiAdapterDescriptor(priority: 0)]
internal sealed class ZenGardenAiAdapter :
    IChatAdapter,
    IEmbedAdapter,
    IOcrAdapter,
    IImagineAdapter,
    ITranscribeAdapter,
    ISpeakAdapter,
    IEditAdapter,
    IRerankAdapter,
    IRenderAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<ZenGardenAiAdapter> _logger;
    private readonly HashSet<string> _discoveredCapabilities;

    public ZenGardenAiAdapter(
        HttpClient http,
        ILogger<ZenGardenAiAdapter> logger,
        IReadOnlySet<string>? discoveredCapabilities = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _discoveredCapabilities = discoveredCapabilities is not null
            ? new HashSet<string>(discoveredCapabilities, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                AiCapability.Chat, AiCapability.Embed, AiCapability.Ocr,
                AiCapability.Vision, AiCapability.Streaming, AiCapability.Tools,
                AiCapability.Imagine, AiCapability.Transcribe, AiCapability.Speak,
                AiCapability.Edit, AiCapability.Rerank, AiCapability.Render,
                AiCapability.Translate, AiCapability.Moderate,
                AiCapability.Pull, AiCapability.ModelList,
            };
    }

    // ── Identity ──────────────────────────────────────────────────

    public string Id => Infrastructure.Constants.Adapter.Id;
    public string Name => Infrastructure.Constants.Adapter.Name;
    public string Type => Infrastructure.Constants.Adapter.Type;

    public IReadOnlySet<string> Capabilities => _discoveredCapabilities;
    public bool HasCapability(string capability) => _discoveredCapabilities.Contains(capability);
    public IAiModelManager? ModelManager => null;

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<JsonElement>(
                Infrastructure.Constants.Endpoints.Models, ct);

            var models = new List<AiModelDescriptor>();
            if (response.TryGetProperty("models", out var modelsArray))
            {
                foreach (var m in modelsArray.EnumerateArray())
                {
                    models.Add(new AiModelDescriptor
                    {
                        Name = m.GetProperty("name").GetString() ?? "",
                        Family = m.TryGetProperty("family", out var f) ? f.GetString() : null,
                        AdapterId = Id,
                        AdapterType = Type,
                    });
                }
            }
            return models;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list models from AI orchestrator");
            return [];
        }
    }

    // ── Chat ──────────────────────────────────────────────────────

    public bool CanServe(AiChatRequest request) => true;

    public async Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
    {
        var body = BuildChatBody(request, stream: false);
        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Chat, body, ct);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        return new AiChatResponse
        {
            Text = root.TryGetProperty("message", out var msg)
                ? msg.GetProperty("content").GetString() ?? ""
                : root.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "",
            Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
            TokensIn = root.TryGetProperty("prompt_eval_count", out var pec) ? pec.GetInt32() : null,
            TokensOut = root.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : null,
            AdapterId = Id,
        };
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildChatBody(request, stream: true);
        var httpReq = new HttpRequestMessage(HttpMethod.Post, Infrastructure.Constants.Endpoints.Chat)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var delta = root.TryGetProperty("message", out var msg)
                ? msg.TryGetProperty("content", out var c) ? c.GetString() : null
                : root.TryGetProperty("response", out var r) ? r.GetString() : null;

            if (!string.IsNullOrEmpty(delta))
            {
                yield return new AiChatChunk { DeltaText = delta, AdapterId = Id };
            }

            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                yield break;
        }
    }

    // ── Embed ─────────────────────────────────────────────────────

    public async Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = request.Model ?? "nomic-embed-text",
            input = request.Input
        });

        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Embed, body, ct);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        var vectors = new List<float[]>();
        if (root.TryGetProperty("embeddings", out var embeddings))
        {
            foreach (var vec in embeddings.EnumerateArray())
            {
                vectors.Add(vec.EnumerateArray().Select(v => v.GetSingle()).ToArray());
            }
        }

        return new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
        };
    }

    // ── OCR ───────────────────────────────────────────────────────

    public async Task<OcrResponse> Recognize(OcrRequest request, CancellationToken ct = default)
    {
        // OCR delegates through Chat with vision — send image via multimodal message
        var chatRequest = new AiChatRequest
        {
            Messages =
            [
                new AiMessage("user", "Extract all text from this image. Return only the extracted text.")
                {
                    Parts =
                    [
                        new AiMessagePart { Type = "text", Text = "Extract all text from this image." },
                        new AiMessagePart { Type = "image", Data = request.Image, MimeType = request.MimeType ?? "image/jpeg" }
                    ]
                }
            ],
            Model = request.Model,
        };

        var response = await Chat(chatRequest, ct);
        return new OcrResponse { Text = response.Text, Model = response.Model };
    }

    // ── Imagine ───────────────────────────────────────────────────

    public async Task<ImagineResponse> Imagine(ImagineRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = request.Model ?? "recommended:imagine",
            prompt = request.Prompt,
            negative = request.Negative,
            width = request.Width,
            height = request.Height,
            guidance = request.Guidance,
            steps = request.Steps,
            seed = request.Seed,
            format = request.Format.ToString().ToLowerInvariant(),
        });

        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Imagine, body, ct);
        var imageBytes = await resp.Content.ReadAsByteArrayAsync(ct);

        return new ImagineResponse
        {
            Image = imageBytes,
            Format = request.Format,
            Model = request.Model,
        };
    }

    // ── Transcribe ────────────────────────────────────────────────

    public async Task<TranscribeResponse> Transcribe(TranscribeRequest request, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(request.Audio), "file", "audio.wav");
        if (request.Model is not null) form.Add(new StringContent(request.Model), "model");
        if (request.Language is not null) form.Add(new StringContent(request.Language), "language");

        using var resp = await _http.PostAsync(Infrastructure.Constants.Endpoints.Transcribe, form, ct);
        resp.EnsureSuccessStatusCode();

        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        return new TranscribeResponse
        {
            Text = root.GetProperty("text").GetString() ?? "",
            Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
        };
    }

    public IAsyncEnumerable<TranscribeSegment>? StreamTranscribe(
        Stream audioStream, TranscribeRequest request, CancellationToken ct = default) => null;

    // ── Speak ─────────────────────────────────────────────────────

    public async Task<SpeakResponse> Speak(SpeakRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = request.Model ?? "tts-1",
            input = request.Text,
            voice = request.Voice ?? "alloy",
            speed = request.Speed,
            response_format = request.Format.ToString().ToLowerInvariant(),
        });

        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Speak, body, ct);
        var audioBytes = await resp.Content.ReadAsByteArrayAsync(ct);

        return new SpeakResponse
        {
            Audio = audioBytes,
            Format = request.Format,
            Model = request.Model,
            Voice = request.Voice,
        };
    }

    public IAsyncEnumerable<ReadOnlyMemory<byte>>? StreamSpeak(
        SpeakRequest request, CancellationToken ct = default) => null;

    // ── Edit ──────────────────────────────────────────────────────

    public async Task<EditResponse> Edit(EditRequest request, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(request.Image), "image", "input.png");
        form.Add(new StringContent(request.Instruction), "instruction");
        if (request.Model is not null) form.Add(new StringContent(request.Model), "model");
        if (request.Mask is not null) form.Add(new ByteArrayContent(request.Mask), "mask", "mask.png");
        if (request.Strength.HasValue) form.Add(new StringContent(request.Strength.Value.ToString()), "strength");

        using var resp = await _http.PostAsync(Infrastructure.Constants.Endpoints.Edit, form, ct);
        resp.EnsureSuccessStatusCode();
        var imageBytes = await resp.Content.ReadAsByteArrayAsync(ct);

        return new EditResponse
        {
            Image = imageBytes,
            Format = request.Format,
            Model = request.Model,
        };
    }

    // ── Rerank ────────────────────────────────────────────────────

    public async Task<RerankResponse> Rerank(RerankRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = request.Query,
            texts = request.Documents,
            model = request.Model ?? "recommended:rerank",
            top_n = request.TopN,
        });

        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Rerank, body, ct);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        var documents = new List<RankedDocument>();
        foreach (var entry in root.EnumerateArray())
        {
            documents.Add(new RankedDocument
            {
                Index = entry.GetProperty("index").GetInt32(),
                Document = request.Documents[entry.GetProperty("index").GetInt32()],
                Score = entry.GetProperty("score").GetDouble(),
            });
        }

        return new RerankResponse
        {
            Documents = documents,
            Model = request.Model,
        };
    }

    // ── Render ────────────────────────────────────────────────────

    public async Task<RenderResponse> Render(RenderRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = request.Model ?? "recommended:render",
            prompt = request.Prompt,
            duration = request.Duration?.TotalSeconds,
            width = request.Width,
            height = request.Height,
            fps = request.Fps,
            seed = request.Seed,
            with_audio = request.WithAudio,
            format = request.Format.ToString().ToLowerInvariant(),
        });

        var timeout = request.Timeout ?? TimeSpan.FromMinutes(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var resp = await PostJson(Infrastructure.Constants.Endpoints.Render, body, cts.Token);
        var videoBytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);

        return new RenderResponse
        {
            Video = videoBytes,
            Format = request.Format,
            Model = request.Model,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static string BuildChatBody(AiChatRequest request, bool stream)
    {
        var messages = request.Messages.Select(m =>
        {
            if (m.Parts is { Count: > 0 })
            {
                return new
                {
                    role = m.Role,
                    content = m.Content,
                    images = m.Parts
                        .Where(p => p.Type == "image" && p.Data is byte[])
                        .Select(p => Convert.ToBase64String((byte[])p.Data!))
                        .ToArray() as object
                };
            }
            return new { role = m.Role, content = m.Content, images = (object?)null };
        });

        return JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages,
            stream,
            options = request.Options is not null ? new
            {
                temperature = request.Options.Temperature,
                num_predict = request.Options.MaxOutputTokens,
                top_p = request.Options.TopP,
                seed = request.Options.Seed,
            } : null,
        });
    }

    private async Task<HttpResponseMessage> PostJson(string path, string json, CancellationToken ct)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(path, content, ct);
        resp.EnsureSuccessStatusCode();
        return resp;
    }
}
