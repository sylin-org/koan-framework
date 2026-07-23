using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Sources;
using Koan.AI.Contracts;
using Koan.AI.Connector.Ollama.Options;

namespace Koan.AI.Connector.Ollama;

internal sealed class OllamaAdapter : IChatAdapter, IEmbedAdapter, IAiSourceInspector, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaAdapter> _logger;
    private readonly OllamaOptions _options;
    private readonly string _defaultModel;
    private readonly SemaphoreSlim? _concurrencyLimiter;
    private readonly ConcurrentDictionary<string, HttpClient> _endpointClients =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public string Id => Infrastructure.Constants.Adapter.Type;
    public string Name => "Ollama AI Provider";
    public string Type => Infrastructure.Constants.Adapter.Type;

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AiCapability.Chat,
        AiCapability.Embed,
        AiCapability.Vision,
        AiCapability.Streaming,
        AiCapability.Tools,
        AiCapability.Pull,
        AiCapability.ModelRemove,
        AiCapability.ModelList,
        AiCapability.ServeGGUF,
    };

    public IAiModelManager? ModelManager => _modelManager ??= new OllamaModelManager(_http, _logger);
    private OllamaModelManager? _modelManager;

    public OllamaAdapter(
        HttpClient http,
        ILogger<OllamaAdapter> logger,
        OllamaOptions? resolvedOptions = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = resolvedOptions ?? new OllamaOptions();
        _defaultModel = _options.DefaultModel;
        if (_options.Endpoints.FirstOrDefault() is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var configuredEndpoint))
        {
            SetDefaultEndpoint(configuredEndpoint);
        }

        if (_options.MaxConcurrentRequests > 0 && _options.MaxConcurrentRequests < int.MaxValue)
        {
            _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);
            _logger.LogInformation(
                "Ollama adapter concurrency limited to {Limit} simultaneous requests",
                _options.MaxConcurrentRequests);
        }
    }

    public bool CanServe(AiChatRequest request) => true;

    internal void SetDefaultEndpoint(Uri? endpoint)
    {
        if (endpoint is not null) _http.BaseAddress = endpoint;
    }

    public async Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
    {
        using var lease = await AcquireConcurrencySlot(ct).ConfigureAwait(false);

        var http = GetHttpClientForRequest(request.InternalConnectionString);
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        }

        var (prompt, imageBase64List) = BuildPromptWithImages(request);
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["options"] = MapOptions(request.Options)
        };

        if (imageBase64List.Count > 0)
        {
            body["images"] = imageBase64List;
        }

        if (request.Options?.Think is bool thinkFlag)
        {
            body["think"] = thinkFlag;
        }

        if (!string.IsNullOrWhiteSpace(request.Options?.ResponseFormat))
        {
            body["format"] = request.Options.ResponseFormat;
        }

        var payload = JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        using var resp = await http.PostAsync("/api/generate", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Ollama: generate failed ({Status}) body={Body}", (int)resp.StatusCode, text);
        }

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonConvert.DeserializeObject<OllamaGenerateResponse>(json)
                  ?? throw new InvalidOperationException("Empty response from Ollama.");

        return new AiChatResponse
        {
            Text = doc.response ?? "",
            FinishReason = doc.done_reason,
            Model = doc.model
        };
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var lease = await AcquireConcurrencySlot(ct).ConfigureAwait(false);

        var http = GetHttpClientForRequest(request.InternalConnectionString);
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        }

        var (prompt, imageBase64List) = BuildPromptWithImages(request);
        var streamBody = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["options"] = MapOptions(request.Options)
        };

        if (imageBase64List.Count > 0)
        {
            streamBody["images"] = imageBase64List;
        }

        if (request.Options?.Think is bool thinkFlag)
        {
            streamBody["think"] = thinkFlag;
        }

        if (!string.IsNullOrWhiteSpace(request.Options?.ResponseFormat))
        {
            streamBody["format"] = request.Options.ResponseFormat;
        }

        var body = JsonConvert.SerializeObject(streamBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await foreach (var part in ReadJsonLinesAsync<OllamaGenerateResponse>(resp, ct).ConfigureAwait(false))
        {
            if (part is null || string.IsNullOrEmpty(part.response))
            {
                continue;
            }

            yield return new AiChatChunk { DeltaText = part.response };
        }
    }

    public async Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        using var lease = await AcquireConcurrencySlot(ct).ConfigureAwait(false);

        var http = GetHttpClientForRequest(request.InternalConnectionString);
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        }

        var vectors = new List<float[]>();
        foreach (var input in request.Input)
        {
            var body = new { model, prompt = input };
            var payload = JsonConvert.SerializeObject(body);

            using var resp = await http.PostAsync("/api/embeddings", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("Ollama: embeddings failed ({Status}) body={Body}", (int)resp.StatusCode, text);
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonConvert.DeserializeObject<OllamaEmbeddingsResponse>(json)
                      ?? throw new InvalidOperationException("Empty response from Ollama.");

            vectors.Add(doc.embedding ?? []);
        }

        var dimension = vectors.FirstOrDefault()?.Length ?? 0;
        return new AiEmbeddingsResponse { Vectors = vectors, Model = model, Dimension = dimension };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
    {
        using var lease = await AcquireConcurrencySlot(ct).ConfigureAwait(false);

        var doc = await FetchTags(_http, ct).ConfigureAwait(false);
        return MapModels(doc);
    }

    public async Task<AiSourceInspection> InspectAsync(
        AiSourceCandidate candidate,
        CancellationToken ct = default)
    {
        try
        {
            using var lease = await AcquireConcurrencySlot(ct).ConfigureAwait(false);
            var endpoint = GetHttpClientForRequest(candidate.Endpoint);
            var models = MapModels(await FetchTags(endpoint, ct).ConfigureAwait(false));
            return new AiSourceInspection
            {
                Provider = Type,
                Endpoint = candidate.Endpoint,
                Available = true,
                Models = models.Select(model => model.Name).Where(name => name.Length > 0).ToArray(),
                Capabilities = new HashSet<string>(Capabilities, StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiSourceInspection
            {
                Provider = Type,
                Endpoint = candidate.Endpoint,
                Available = false,
                Capabilities = new HashSet<string>(Capabilities, StringComparer.OrdinalIgnoreCase),
                Detail = ex.Message
            };
        }
    }

    private IReadOnlyList<AiModelDescriptor> MapModels(OllamaTagsResponse? doc)
    {
        var models = new List<AiModelDescriptor>();

        foreach (var model in doc?.models ?? Enumerable.Empty<OllamaTag>())
        {
            models.Add(new AiModelDescriptor
            {
                Name = model.name ?? "",
                Family = model.model,
                AdapterId = Id,
                AdapterType = Type
            });
        }

        return models;
    }

    private async Task<OllamaTagsResponse?> FetchTags(HttpClient http, CancellationToken ct)
    {
        using var resp = await http.GetAsync("/api/tags", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Ollama: tags failed ({Status}) body={Body}", (int)resp.StatusCode, text);
            resp.EnsureSuccessStatusCode();
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<OllamaTagsResponse>(json);
    }

    private HttpClient GetHttpClientForRequest(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var endpoint = new Uri(connectionString).ToString().TrimEnd('/');
            if (_http.BaseAddress is not null &&
                string.Equals(
                    _http.BaseAddress.ToString().TrimEnd('/'),
                    endpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return _http;
            }

            return _endpointClients.GetOrAdd(endpoint, static value => new HttpClient
            {
                BaseAddress = new Uri(value),
                Timeout = Timeout.InfiniteTimeSpan
            });
        }

        return _http;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var client in _endpointClients.Values) client.Dispose();
        _endpointClients.Clear();
        _http.Dispose();
        _concurrencyLimiter?.Dispose();
    }

    private async Task<SemaphoreReleaser?> AcquireConcurrencySlot(CancellationToken ct)
    {
        if (_concurrencyLimiter is null)
        {
            return null;
        }

        await _concurrencyLimiter.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreReleaser(_concurrencyLimiter);
    }

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }

    private static (string prompt, List<string> images) BuildPromptWithImages(AiChatRequest req)
    {
        var images = new List<string>();

        if (req.Messages.Count == 1 && string.Equals(req.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            var (content, messageImages) = ResolveMessageContentWithImages(req.Messages[0]);
            images.AddRange(messageImages);
            return (content, images);
        }

        var sb = new StringBuilder();
        foreach (var m in req.Messages)
        {
            var (content, messageImages) = ResolveMessageContentWithImages(m);
            images.AddRange(messageImages);

            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[system]\n{content}\n");
            }
            else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[user]\n{content}\n");
            }
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[assistant]\n{content}\n");
            }
        }

        return (sb.ToString(), images);
    }

    private static (string content, List<string> images) ResolveMessageContentWithImages(AiMessage message)
    {
        var images = new List<string>();

        if (message.Parts is { Count: > 0 })
        {
            var textBuilder = new StringBuilder();
            foreach (var part in message.Parts)
            {
                if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    textBuilder.Append(part.Text);
                    continue;
                }

                if (part.Data is not null)
                {
                    if (part.Data is byte[] imageBytes)
                    {
                        var base64 = Convert.ToBase64String(imageBytes);
                        images.Add(base64);
                    }
                    else if (part.Data is string str && !string.IsNullOrWhiteSpace(str))
                    {
                        if (str.Length > 100 && !str.Contains(' '))
                        {
                            images.Add(str);
                        }
                        else
                        {
                            textBuilder.Append(str);
                        }
                    }
                    else
                    {
                        textBuilder.Append(JsonConvert.SerializeObject(part.Data));
                    }
                }
            }

            var text = textBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return (text, images);
            }
        }

        return (message.Content, images);
    }

    private static IDictionary<string, object?> MapOptions(AiPromptOptions? o)
    {
        if (o is null)
        {
            return new Dictionary<string, object?>();
        }

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
                dict[normKey] = kv.Value.Type switch
                {
                    JTokenType.String => kv.Value.Value<string>(),
                    JTokenType.Integer => kv.Value.Value<long>(),
                    JTokenType.Float => kv.Value.Value<double>(),
                    JTokenType.Boolean => kv.Value.Value<bool>(),
                    JTokenType.Array => kv.Value,
                    JTokenType.Object => kv.Value,
                    _ => null
                };
            }
        }

        return dict;
    }

    private static string NormalizeOllamaOptionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var k = key.Trim();
        if (k.Equals("temp", StringComparison.OrdinalIgnoreCase) || k.Equals("temperature", StringComparison.OrdinalIgnoreCase))
        {
            return "temperature";
        }

        if (k.Equals("top_p", StringComparison.OrdinalIgnoreCase) || k.Equals("topp", StringComparison.OrdinalIgnoreCase) || k.Equals("topP", StringComparison.Ordinal))
        {
            return "top_p";
        }

        if (k.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("max_new_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("maxOutputTokens", StringComparison.Ordinal) ||
            k.Equals("max_output_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("num_predict", StringComparison.OrdinalIgnoreCase))
        {
            return "num_predict";
        }

        if (k.Equals("stop_sequences", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("stopSequences", StringComparison.Ordinal) ||
            k.Equals("stops", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return "stop";
        }

        if (k.Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            return "seed";
        }

        return k;
    }

    private static async IAsyncEnumerable<T?> ReadJsonLinesAsync<T>(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            T? obj;
            try
            {
                obj = JsonConvert.DeserializeObject<T>(line);
            }
            catch
            {
                continue;
            }

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
