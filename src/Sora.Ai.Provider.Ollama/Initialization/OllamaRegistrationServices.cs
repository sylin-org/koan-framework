using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Ai.Provider.Ollama.Options;
using Sora.AI.Contracts.Routing;

namespace Sora.Ai.Provider.Ollama.Initialization;

internal sealed class OllamaConfigRegistrationService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiAdapterRegistry _registry;
    public OllamaConfigRegistrationService(IServiceProvider sp, IConfiguration cfg, IAiAdapterRegistry registry)
    { _sp = sp; _cfg = cfg; _registry = registry; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var items = _cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot).Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();
            foreach (var opt in items.Where(i => i.Enabled))
            {
                try
                {
                    var http = new HttpClient { BaseAddress = new Uri(opt.BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
                    var logger = _sp.GetService<Microsoft.Extensions.Logging.ILogger<OllamaAdapter>>();
                    var adapter = new OllamaAdapter(opt.Id, $"Ollama ({http.BaseAddress})", http, opt.DefaultModel, logger);
                    _registry.Add(adapter);
                }
                catch { /* ignore invalid entries */ }
            }
        }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class OllamaDiscoveryService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiAdapterRegistry _registry;
    public OllamaDiscoveryService(IServiceProvider sp, IConfiguration cfg, IAiAdapterRegistry registry)
    { _sp = sp; _cfg = cfg; _registry = registry; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var envIsDev = Core.SoraEnv.IsDevelopment;
            var aiOpts = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.AI.Contracts.Options.AiOptions>>()?.Value;
            var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
            // Provider-scoped default: allow discovery in non-dev unless explicitly disabled via AiOptions
            var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;
            if (!autoDiscovery) return Task.CompletedTask;
            if (!envIsDev && !allowNonDev) return Task.CompletedTask;

            // If explicit Ollama services are configured, do not auto-discover
            try
            {
                var configured = _cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot)
                    .Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();
                if (configured.Any(s => s.Enabled))
                    return Task.CompletedTask;
            }
            catch { /* ignore and proceed with discovery */ }

            // If the app requires specific models, use the first as the default for discovered adapters
            string? defaultModel = null;
            try { defaultModel = _cfg.GetSection("Sora:Ai:Ollama:RequiredModels").Get<string[]>()?.FirstOrDefault(); } catch { }

            foreach (var u in CollectCandidateUrls(_cfg))
            {
                try
                {
                    using var http = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromMilliseconds(400) };
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
                    var resp = http.GetAsync(Infrastructure.Constants.Discovery.TagsPath, cts.Token).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) continue;

                    // If a required model is specified, ensure the endpoint has it before registering
                    if (!string.IsNullOrWhiteSpace(defaultModel))
                    {
                        try
                        {
                            var payload = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                            if (!EndpointHasModel(payload, defaultModel))
                                continue; // try next candidate
                        }
                        catch { /* best-effort filter */ }
                    }
                    var client = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromSeconds(60) };
                    var id = $"ollama@{u.Host}:{u.Port}";
                    var logger = _sp.GetService<Microsoft.Extensions.Logging.ILogger<OllamaAdapter>>();
                    _registry.Add(new OllamaAdapter(id, $"Ollama ({u})", client, defaultModel: defaultModel, logger));
                    // Register only the first viable endpoint (host-first policy)
                    break;
                }
                catch { /* ignore */ }
            }
        }
        catch { /* best-effort */ }
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
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return false;
            foreach (var m in models.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
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
