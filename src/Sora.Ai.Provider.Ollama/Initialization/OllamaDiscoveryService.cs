using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sora.Ai.Provider.Ollama.Options;
using Sora.AI.Contracts.Routing;

namespace Sora.Ai.Provider.Ollama.Initialization;

internal sealed class OllamaDiscoveryService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<OllamaDiscoveryService> _logger;
    public OllamaDiscoveryService(IServiceProvider sp, IConfiguration cfg, IAiAdapterRegistry registry)
    { 
        _sp = sp; 
        _cfg = cfg; 
        _registry = registry; 
        _logger = sp.GetService<ILogger<OllamaDiscoveryService>>() 
                 ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaDiscoveryService>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("OllamaDiscoveryService starting...");
            var envIsDev = Core.SoraEnv.IsDevelopment;
            var aiOpts = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.AI.Contracts.Options.AiOptions>>()?.Value;
            var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
            // Provider-scoped default: allow discovery in non-dev unless explicitly disabled via AiOptions
            var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;
            _logger.LogDebug("Discovery settings: envIsDev={EnvIsDev}, autoDiscovery={AutoDiscovery}, allowNonDev={AllowNonDev}", envIsDev, autoDiscovery, allowNonDev);
            if (!autoDiscovery) 
            {
                _logger.LogDebug("Auto-discovery disabled, skipping");
                return Task.CompletedTask;
            }
            if (!envIsDev && !allowNonDev) 
            {
                _logger.LogDebug("Non-dev environment and discovery not allowed in non-dev, skipping");
                return Task.CompletedTask;
            }

            // If explicit Ollama services are configured, do not auto-discover
            try
            {
                var configured = _cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot)
                    .Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();
                _logger.LogDebug("Found {ConfigCount} configured Ollama services", configured.Length);
                var enabledCount = configured.Count(s => s.Enabled);
                _logger.LogDebug("Found {EnabledCount} enabled configured Ollama services", enabledCount);
                if (configured.Any(s => s.Enabled))
                {
                    _logger.LogDebug("Explicit enabled services found, skipping auto-discovery");
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex) 
            { 
                _logger.LogDebug(ex, "Error checking configured services, proceeding with discovery");
            }

            // If the app requires specific models, use the first as the default for discovered adapters
            string? defaultModel = null;
            try { defaultModel = _cfg.GetSection("Sora:Ai:Ollama:RequiredModels").Get<string[]>()?.FirstOrDefault(); } catch { }
            _logger.LogDebug("Default model for discovered adapters: {DefaultModel}", defaultModel ?? "none");

            var candidateUrls = CollectCandidateUrls(_cfg).ToList();
            _logger.LogDebug("Testing {UrlCount} candidate URLs for Ollama discovery", candidateUrls.Count);
            
            foreach (var u in candidateUrls)
            {
                _logger.LogDebug("Testing Ollama endpoint: {Url}", u);
                try
                {
                    using var http = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromMilliseconds(400) };
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
                    var resp = http.GetAsync(Infrastructure.Constants.Discovery.TagsPath, cts.Token).GetAwaiter().GetResult();
                    _logger.LogDebug("Response from {Url}: {StatusCode}", u, resp.StatusCode);
                    if (!resp.IsSuccessStatusCode) continue;

                    // If a required model is specified, ensure the endpoint has it before registering
                    if (!string.IsNullOrWhiteSpace(defaultModel))
                    {
                        try
                        {
                            var payload = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                            var hasModel = EndpointHasModel(payload, defaultModel);
                            _logger.LogDebug("Endpoint {Url} has required model '{Model}': {HasModel}", u, defaultModel, hasModel);
                            if (!hasModel)
                                continue; // try next candidate
                        }
                        catch (Exception ex) 
                        { 
                            _logger.LogDebug(ex, "Error checking model availability at {Url}, proceeding anyway", u);
                        }
                    }
                    var client = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromSeconds(60) };
                    var id = $"ollama@{u.Host}:{u.Port}";
                    var logger = _sp.GetService<Microsoft.Extensions.Logging.ILogger<OllamaAdapter>>();
                    var adapter = new OllamaAdapter(id, $"Ollama ({u})", client, defaultModel: defaultModel, logger);
                    _logger.LogDebug("Registering Ollama adapter: {AdapterId} at {Url}", id, u);
                    _registry.Add(adapter);
                    // Register only the first viable endpoint (host-first policy)
                    _logger.LogDebug("Successfully registered first viable Ollama endpoint, stopping discovery");
                    break;
                }
                catch (Exception ex) 
                { 
                    _logger.LogDebug(ex, "Error testing endpoint {Url}", u);
                }
            }
        }
        catch (Exception ex) 
        { 
            _logger.LogError(ex, "Unexpected error in OllamaDiscoveryService");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static IEnumerable<Uri> CollectCandidateUrls(IConfiguration? cfg)
    {
        // Preserve insertion order while de-duplicating
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void Add(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var u = url.Trim();
            if (seen.Add(u)) ordered.Add(u);
        }
        // Highest precedence: explicit single var
        var fromEnv = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvBaseUrl);
        Add(fromEnv ?? string.Empty);
        // Next: multi-endpoint env list, keep given order
        var multi = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(multi))
        {
            foreach (var part in multi.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        // Finally: sensible defaults in strict host-first order
        Add($"http://{Infrastructure.Constants.Discovery.Localhost}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.Loopback}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.HostDocker}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.WellKnownServiceName}:{Infrastructure.Constants.Discovery.DefaultPort}");
        foreach (var s in ordered)
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri)) yield return uri;
        }
    }

    private static bool EndpointHasModel(string json, string required)
    {
        // Accept either exact name or prefix before ':' tag (e.g., "all-minilm" matches "all-minilm:latest")
        try
        {
            var doc = JToken.Parse(json);
            var models = doc["models"] as JArray;
            if (models is null) return false;
            foreach (var m in models)
            {
                var name = m?["name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                var baseName = name.Split(':')[0];
                if (string.Equals(name, required, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, required, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
}