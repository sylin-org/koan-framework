using System;
using System.Collections.Generic;
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

namespace Koan.AI.Connector.Ollama.Infrastructure;

internal sealed class OllamaModelManager : IAiModelManager
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _adapterId;
    private readonly string _adapterType;

    public OllamaModelManager(HttpClient http, ILogger logger, string adapterId, string adapterType)
    {
        _http = http;
        _logger = logger;
        _adapterId = adapterId;
        _adapterType = adapterType;
    }

    public async Task<AiModelOperationResult> EnsureInstalledAsync(AiModelOperationRequest request, CancellationToken ct = default)
        => await PullModelAsync(request, skipIfPresent: true, successMessage: "Model installed.", ct).ConfigureAwait(false);

    public async Task<AiModelOperationResult> RefreshAsync(AiModelOperationRequest request, CancellationToken ct = default)
        => await PullModelAsync(request, skipIfPresent: false, successMessage: "Model refreshed.", ct).ConfigureAwait(false);

    public async Task<AiModelOperationResult> FlushAsync(AiModelOperationRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var identifier = ComposeModelIdentifier(request);

        try
        {
            if (!await ModelExistsAsync(identifier, ct).ConfigureAwait(false))
            {
                return AiModelOperationResult.Succeeded(CreateDescriptor(identifier), performed: false, message: $"Model '{identifier}' was already absent.");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { name = identifier }), Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Delete request for '{identifier}' failed with status {(int)response.StatusCode}: {body}");
            }

            _logger.LogInformation("[{AdapterId}] Removed Ollama model '{Model}'", _adapterId, identifier);
            return AiModelOperationResult.Succeeded(CreateDescriptor(identifier), performed: true, message: $"Model '{identifier}' removed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AdapterId}] Failed to remove Ollama model '{Model}'", _adapterId, identifier);
            return AiModelOperationResult.Failed(ex.Message);
        }
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListManagedModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var names = await FetchModelNamesAsync(ct).ConfigureAwait(false);
            return names.Select(CreateDescriptor).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AdapterId}] Failed to enumerate Ollama models", _adapterId);
            throw;
        }
    }

    private async Task<AiModelOperationResult> PullModelAsync(AiModelOperationRequest request, bool skipIfPresent, string successMessage, CancellationToken ct)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("Model name is required.", nameof(request));
        }

        var identifier = ComposeModelIdentifier(request);

        try
        {
            if (skipIfPresent && await ModelExistsAsync(identifier, ct).ConfigureAwait(false))
            {
                return AiModelOperationResult.Succeeded(CreateDescriptor(identifier), performed: false, message: $"Model '{identifier}' already present.");
            }

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = identifier,
                ["stream"] = false
            };

            if (request.Parameters is { Count: > 0 })
            {
                foreach (var kv in request.Parameters)
                {
                    payload[kv.Key] = kv.Value;
                }
            }

            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("/api/pull", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Pull request for '{identifier}' failed with status {(int)response.StatusCode}: {body}");
            }

            _logger.LogInformation("[{AdapterId}] Pulled Ollama model '{Model}'", _adapterId, identifier);
            return AiModelOperationResult.Succeeded(CreateDescriptor(identifier), performed: true, message: successMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AdapterId}] Failed to pull Ollama model '{Model}'", _adapterId, identifier);
            return AiModelOperationResult.Failed(ex.Message);
        }
    }

    private async Task<bool> ModelExistsAsync(string identifier, CancellationToken ct)
    {
        var models = await FetchModelNamesAsync(ct).ConfigureAwait(false);
        return models.Any(name => MatchesModel(name, identifier));
    }

    private async Task<IReadOnlyList<string>> FetchModelNamesAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Tags request failed with status {(int)response.StatusCode}: {body}");
        }

        try
        {
            var doc = JObject.Parse(body);
            var models = doc["models"] as JArray;
            if (models is null)
            {
                return Array.Empty<string>();
            }

            return models
                .Select(m => m?["name"]?.Value<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Unable to parse Ollama tags payload.", ex);
        }
    }

    private AiModelDescriptor CreateDescriptor(string identifier)
        => new()
        {
            Name = StripRepository(identifier),
            AdapterId = _adapterId,
            AdapterType = _adapterType
        };

    private static string ComposeModelIdentifier(AiModelOperationRequest request)
    {
        var model = request.Model.Trim();

        if (!string.IsNullOrWhiteSpace(request.Version) && !model.Contains(':', StringComparison.Ordinal))
        {
            model = $"{model}:{request.Version.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(request.Repository))
        {
            var repo = request.Repository.Trim().TrimEnd('/');
            if (!model.StartsWith(repo, StringComparison.OrdinalIgnoreCase))
            {
                model = $"{repo}/{model}";
            }
        }

        return model;
    }

    private static bool MatchesModel(string candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidateCore = StripRepository(candidate);
        var expectedCore = StripRepository(expected);

        if (string.Equals(candidateCore, expectedCore, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(RemoveVersion(candidateCore), RemoveVersion(expectedCore), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripRepository(string value)
    {
        var slash = value.LastIndexOf('/');
        return slash >= 0 ? value[(slash + 1)..] : value;
    }

    private static string RemoveVersion(string value)
    {
        var colon = value.IndexOf(':');
        return colon >= 0 ? value[..colon] : value;
    }
}

