using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;

namespace Koan.AI.Connector.Ollama;

/// <summary>
/// Model manager for Ollama that handles pull, delete, and refresh operations
/// via the Ollama REST API.
/// </summary>
internal sealed class OllamaModelManager : IAiModelManager
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public OllamaModelManager(HttpClient http, ILogger logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiModelOperationResult> EnsureInstalled(AiModelOperationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return AiModelOperationResult.Failed("Model name is required.");
        }

        _logger.LogInformation("Ollama: pulling model '{Model}'", request.Model);

        var payload = BuildPullPayload(request, stream: false);
        using var resp = await _http.PostAsync("/api/pull", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Ollama: pull failed ({Status}) body={Body}", (int)resp.StatusCode, body);
            return AiModelOperationResult.Failed($"Pull failed with status {(int)resp.StatusCode}: {body}");
        }

        var descriptor = new AiModelDescriptor
        {
            Name = request.Model,
            AdapterId = "ollama",
            AdapterType = "ollama"
        };

        return AiModelOperationResult.Succeeded(descriptor, performed: true, "Model pulled successfully.");
    }

    public async Task<AiModelOperationResult> Refresh(AiModelOperationRequest request, CancellationToken ct = default)
    {
        // Ollama re-pull is the same endpoint — it will re-download if needed
        return await EnsureInstalled(request, ct).ConfigureAwait(false);
    }

    public async Task<AiModelOperationResult> Flush(AiModelOperationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return AiModelOperationResult.Failed("Model name is required.");
        }

        _logger.LogInformation("Ollama: deleting model '{Model}'", request.Model);

        var body = JsonConvert.SerializeObject(new { name = request.Model });
        using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Ollama: delete failed ({Status}) body={Body}", (int)resp.StatusCode, respBody);
            return AiModelOperationResult.Failed($"Delete failed with status {(int)resp.StatusCode}: {respBody}");
        }

        var descriptor = new AiModelDescriptor
        {
            Name = request.Model,
            AdapterId = "ollama",
            AdapterType = "ollama"
        };

        return AiModelOperationResult.Succeeded(descriptor, performed: true, "Model deleted successfully.");
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListManagedModels(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Ollama: tags failed ({Status}) body={Body}", (int)resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonConvert.DeserializeObject<OllamaTagsResponse>(json);

        return (doc?.models ?? Enumerable.Empty<OllamaTag>())
            .Select(m => new AiModelDescriptor
            {
                Name = m.name ?? "",
                Family = m.model,
                AdapterId = "ollama",
                AdapterType = "ollama"
            })
            .ToList();
    }

    private static string BuildPullPayload(AiModelOperationRequest request, bool stream)
    {
        var model = request.Model;
        if (!string.IsNullOrWhiteSpace(request.Version) && !model.Contains(':'))
        {
            model = $"{model}:{request.Version}";
        }

        var insecure = false;
        if (request.Parameters?.TryGetValue("insecure", out var insecureVal) == true)
        {
            _ = bool.TryParse(insecureVal, out insecure);
        }

        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = model,
            ["stream"] = stream
        };

        if (insecure)
        {
            payload["insecure"] = true;
        }

        return JsonConvert.SerializeObject(payload);
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
