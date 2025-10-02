using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.Core.Adapters;
using Koan.Core.Logging;
using Koan.Core.Orchestration;

namespace Koan.AI.Connector.Ollama.Initialization;

/// <summary>
/// ADR-0015 compliant Ollama discovery service.
/// Creates source "ollama" (priority 50) with auto-discovered members.
/// Implements Urls vs AdditionalUrls semantics.
/// NO duplicate adapter registration - adapters are singletons managed by DI.
/// </summary>
internal sealed class OllamaDiscoveryService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly ILogger<OllamaDiscoveryService> _logger;
    private readonly IOrchestrationAwareServiceDiscovery _serviceDiscovery;
    private const string CacheDirectory = "/app/cache/ai-introspection";

    public OllamaDiscoveryService(IServiceProvider sp, IConfiguration cfg, IAiSourceRegistry sourceRegistry)
    {
        _sp = sp;
        _cfg = cfg;
        _sourceRegistry = sourceRegistry;
        _logger = sp.GetService<ILogger<OllamaDiscoveryService>>()
                 ?? NullLogger<OllamaDiscoveryService>.Instance;
        _serviceDiscovery = new OrchestrationAwareServiceDiscovery(cfg, null);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            KoanLog.BootInfo(_logger, LogActions.Discovery, "start");

            // Check if discovery should be enabled
            if (!ShouldPerformDiscovery())
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "auto-discovery-disabled"));
                return;
            }

            // Check if source "ollama" already exists (from explicit config or prior registration)
            if (_sourceRegistry.HasSource("ollama"))
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "source-already-exists"));
                return;
            }

            var ollamaConfig = _cfg.GetSection("Koan:Ai:Ollama");
            var defaultModel = GetDefaultModel(ollamaConfig);

            // ADR-0015: Urls = explicit only, AdditionalUrls = extend discovery
            var explicitUrls = ollamaConfig.GetSection("Urls").Get<string[]>();
            var additionalUrls = ollamaConfig.GetSection("AdditionalUrls").Get<string[]>();

            // Validate configuration
            ValidateConfiguration(explicitUrls, additionalUrls);

            // Build member list
            var members = new List<AiMemberDefinition>();

            if (explicitUrls?.Length > 0)
            {
                // Explicit mode: NO discovery, only use configured URLs
                KoanLog.BootInfo(_logger, LogActions.Discovery, "explicit-mode",
                    ("count", explicitUrls.Length));

                for (int i = 0; i < explicitUrls.Length; i++)
                {
                    var url = explicitUrls[i];
                    var capabilities = await GetCapabilitiesAsync(url, defaultModel, cancellationToken);

                    members.Add(new AiMemberDefinition
                    {
                        Name = $"ollama::explicit-{i + 1}",
                        ConnectionString = url,
                        Order = i,
                        Capabilities = capabilities,
                        Origin = "config-urls",
                        IsAutoDiscovered = false
                    });
                }
            }
            else
            {
                // Discovery mode: Auto-discover + optional AdditionalUrls
                KoanLog.BootInfo(_logger, LogActions.Discovery, "discovery-mode");

                var discovered = await DiscoverOllamaInstances(defaultModel, cancellationToken);
                members.AddRange(discovered);

                KoanLog.BootInfo(_logger, LogActions.Discovery, "discovered",
                    ("count", discovered.Count));

                // Add additional URLs if specified
                if (additionalUrls?.Length > 0)
                {
                    KoanLog.BootInfo(_logger, LogActions.Discovery, "additional-urls",
                        ("count", additionalUrls.Length));

                    for (int i = 0; i < additionalUrls.Length; i++)
                    {
                        var url = additionalUrls[i];
                        var capabilities = await GetCapabilitiesAsync(url, defaultModel, cancellationToken);

                        members.Add(new AiMemberDefinition
                        {
                            Name = $"ollama::additional-{i + 1}",
                            ConnectionString = url,
                            Order = discovered.Count + i,
                            Capabilities = capabilities,
                            Origin = "config-additional-urls",
                            IsAutoDiscovered = false
                        });
                    }
                }
            }

            if (members.Count == 0)
            {
                KoanLog.BootWarning(_logger, LogActions.Discovery, "no-members",
                    ("reason", "no-instances-found"));
                return;
            }

            // Get policy (with precedence: source-specific → adapter-level → global)
            var policy = ResolvePolicy(ollamaConfig);

            // Create source "ollama" with discovered/configured members
            var source = new AiSourceDefinition
            {
                Name = "ollama",
                Provider = "ollama",
                Priority = 50, // Adapter-provided sources default to 50
                Policy = policy,
                Members = members,
                Capabilities = BuildSourceCapabilities(members, defaultModel),
                Origin = explicitUrls?.Length > 0 ? "explicit-config" : "auto-discovery",
                IsAutoDiscovered = explicitUrls == null || explicitUrls.Length == 0
            };

            _sourceRegistry.RegisterSource(source);

            KoanLog.BootInfo(_logger, LogActions.Discovery, "source-registered",
                ("source", "ollama"),
                ("members", members.Count),
                ("policy", policy),
                ("priority", 50));

            // Log each member
            foreach (var member in members)
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "member",
                    ("name", member.Name),
                    ("url", member.ConnectionString),
                    ("capabilities", member.Capabilities?.Count ?? 0));
            }
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "unexpected-error", ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "error-detail", ("exception", ex.ToString()));
        }
    }

    private bool ShouldPerformDiscovery()
    {
        var envIsDev = Core.KoanEnv.IsDevelopment;
        var aiOpts = _sp.GetService<IOptions<Koan.AI.Contracts.Options.AiOptions>>()?.Value;
        var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
        var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;

        if (!autoDiscovery)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "disabled", ("reason", "AutoDiscoveryEnabled=false"));
            return false;
        }

        if (!envIsDev && !allowNonDev)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "disabled", ("reason", "non-dev-blocked"));
            return false;
        }

        return true;
    }

    private string? GetDefaultModel(IConfigurationSection ollamaConfig)
    {
        // Canonical path: Koan:Ai:Ollama:DefaultModel
        var defaultModel = ollamaConfig["DefaultModel"];
        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "default-model", ("model", defaultModel));
            return defaultModel;
        }

        // Fallback: RequiredModels[0] for backward compatibility
        var requiredModels = ollamaConfig.GetSection("RequiredModels").Get<string[]>();
        var fallbackModel = requiredModels?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fallbackModel))
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "fallback-model", ("model", fallbackModel));
            return fallbackModel;
        }

        return null;
    }

    private void ValidateConfiguration(string[]? explicitUrls, string[]? additionalUrls)
    {
        // Error: Both Urls and AdditionalUrls specified
        if (explicitUrls?.Length > 0 && additionalUrls?.Length > 0)
        {
            _logger.LogWarning(
                "Both 'Urls' and 'AdditionalUrls' specified for Ollama - 'AdditionalUrls' will be ignored (explicit mode active)");
        }

        // Error: Empty Urls array
        if (explicitUrls?.Length == 0)
        {
            throw new InvalidOperationException(
                "Koan:Ai:Ollama:Urls is empty. Remove the key to enable auto-discovery, or provide at least one URL.");
        }
    }

    private async Task<List<AiMemberDefinition>> DiscoverOllamaInstances(string? defaultModel, CancellationToken ct)
    {
        var candidates = new[]
        {
            ("ollama::host", "http://host.docker.internal:11434", 0),
            ("ollama::linked", "http://ollama:11434", 1),
            ("ollama::container", "http://localhost:11434", 2)
        };

        var discovered = new List<AiMemberDefinition>();

        foreach (var (name, url, order) in candidates)
        {
            if (await IsHealthy(url, ct))
            {
                var capabilities = await GetCapabilitiesAsync(url, defaultModel, ct);

                discovered.Add(new AiMemberDefinition
                {
                    Name = name,
                    ConnectionString = url,
                    Order = order,
                    Capabilities = capabilities,
                    Origin = "discovered",
                    IsAutoDiscovered = true
                });

                KoanLog.BootDebug(_logger, LogActions.Discovery, "candidate-success",
                    ("member", name),
                    ("url", url));
            }
            else
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "candidate-failed",
                    ("member", name),
                    ("url", url));
            }
        }

        return discovered;
    }

    private async Task<bool> IsHealthy(string url, CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var response = await httpClient.GetAsync($"{url}/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ADR-0015: Lazy introspection with persistent caching.
    /// Cache location: /app/cache/ai-introspection/{hash(url)}.json
    /// Cache TTL: 24 hours
    /// </summary>
    private async Task<IReadOnlyDictionary<string, AiCapabilityConfig>> GetCapabilitiesAsync(
        string url,
        string? defaultModel,
        CancellationToken ct)
    {
        // 1. Check cache
        var cacheKey = ComputeHash(url);
        var cachePath = Path.Combine(CacheDirectory, $"{cacheKey}.json");

        if (File.Exists(cachePath))
        {
            try
            {
                var cacheJson = await File.ReadAllTextAsync(cachePath, ct);
                var cached = JsonSerializer.Deserialize<CachedCapabilities>(cacheJson);

                if (cached != null && (DateTime.UtcNow - cached.IntrospectedAt).TotalHours < 24)
                {
                    _logger.LogDebug(
                        "Using cached capabilities for {Url} (age: {Age:F1}h)",
                        url,
                        (DateTime.UtcNow - cached.IntrospectedAt).TotalHours);

                    return cached.Capabilities;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Cache read failed for {Url}: {Error}", url, ex.Message);
            }
        }

        // 2. Introspect via /api/tags
        var capabilities = await IntrospectCapabilities(url, defaultModel, ct);

        // 3. Cache result
        try
        {
            var cacheData = new CachedCapabilities
            {
                Url = url,
                IntrospectedAt = DateTime.UtcNow,
                Capabilities = capabilities
            };

            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllTextAsync(
                cachePath,
                JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = true }),
                ct);

            _logger.LogDebug("Cached capabilities for {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Cache write failed for {Url}: {Error}", url, ex.Message);
        }

        return capabilities;
    }

    private async Task<Dictionary<string, AiCapabilityConfig>> IntrospectCapabilities(
        string url,
        string? defaultModel,
        CancellationToken ct)
    {
        var capabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync($"{url}/api/tags", ct);

            if (!response.IsSuccessStatusCode)
            {
                // No models available - use default if provided
                if (!string.IsNullOrWhiteSpace(defaultModel))
                {
                    capabilities["Chat"] = new AiCapabilityConfig { Model = defaultModel };
                    capabilities["Embedding"] = new AiCapabilityConfig { Model = defaultModel };
                }
                return capabilities;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JToken.Parse(json);
            var models = doc["models"] as JArray;

            if (models == null || models.Count == 0)
            {
                return capabilities;
            }

            // Extract model names
            var modelNames = models
                .Select(m => m?["name"]?.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();

            // Preferred models for each capability
            var chatPreferred = new[] { "llama3.2", "llama3.1", "llama3", "llama2", "mistral", "qwen" };
            var embedPreferred = new[] { "nomic-embed-text", "all-minilm", "bge-", "mxbai-embed" };
            var visionPreferred = new[] { "llava", "bakllava", "vision" };

            // Map capabilities intelligently
            var chatModel = FindPreferredModel(modelNames, chatPreferred) ?? defaultModel ?? modelNames.FirstOrDefault();
            var embedModel = FindPreferredModel(modelNames, embedPreferred) ?? defaultModel ?? modelNames.FirstOrDefault();
            var visionModel = FindPreferredModel(modelNames, visionPreferred);

            if (!string.IsNullOrWhiteSpace(chatModel))
            {
                capabilities["Chat"] = new AiCapabilityConfig { Model = chatModel };
            }

            if (!string.IsNullOrWhiteSpace(embedModel))
            {
                capabilities["Embedding"] = new AiCapabilityConfig { Model = embedModel };
            }

            if (!string.IsNullOrWhiteSpace(visionModel))
            {
                capabilities["Vision"] = new AiCapabilityConfig { Model = visionModel };
            }
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "introspection-failed",
                ("url", url),
                ("reason", ex.Message));

            // Fallback to default if provided
            if (!string.IsNullOrWhiteSpace(defaultModel))
            {
                capabilities["Chat"] = new AiCapabilityConfig { Model = defaultModel };
                capabilities["Embedding"] = new AiCapabilityConfig { Model = defaultModel };
            }
        }

        return capabilities;
    }

    private static string? FindPreferredModel(List<string> available, string[] preferred)
    {
        foreach (var pref in preferred)
        {
            var match = available.FirstOrDefault(m =>
                m.StartsWith(pref, StringComparison.OrdinalIgnoreCase) ||
                m.Split(':')[0].StartsWith(pref, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;
        }

        return null;
    }

    private IReadOnlyDictionary<string, AiCapabilityConfig> BuildSourceCapabilities(
        List<AiMemberDefinition> members,
        string? defaultModel)
    {
        // Aggregate capabilities from all members
        var allCapabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members)
        {
            if (member.Capabilities != null)
            {
                foreach (var (capName, capConfig) in member.Capabilities)
                {
                    if (!allCapabilities.ContainsKey(capName))
                    {
                        allCapabilities[capName] = capConfig;
                    }
                }
            }
        }

        // If no capabilities found and defaultModel provided, add defaults
        if (allCapabilities.Count == 0 && !string.IsNullOrWhiteSpace(defaultModel))
        {
            allCapabilities["Chat"] = new AiCapabilityConfig { Model = defaultModel };
            allCapabilities["Embedding"] = new AiCapabilityConfig { Model = defaultModel };
        }

        return allCapabilities;
    }

    private string ResolvePolicy(IConfigurationSection ollamaConfig)
    {
        // Policy precedence: source-specific → adapter-level → global
        var sourcePolicy = ollamaConfig["Policy"];
        if (!string.IsNullOrWhiteSpace(sourcePolicy))
            return sourcePolicy;

        var globalConfig = _cfg.GetSection("Koan:Ai");
        var adapterPolicy = globalConfig["Ollama:Policy"];
        if (!string.IsNullOrWhiteSpace(adapterPolicy))
            return adapterPolicy;

        var globalPolicy = globalConfig["Policy"];
        if (!string.IsNullOrWhiteSpace(globalPolicy))
            return globalPolicy;

        return "Fallback"; // Framework default
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
    }

    private record CachedCapabilities
    {
        public string Url { get; init; } = "";
        public DateTime IntrospectedAt { get; init; }
        public Dictionary<string, AiCapabilityConfig> Capabilities { get; init; } = new();
    }

    private static class LogActions
    {
        public const string Discovery = "ollama.discovery";
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
