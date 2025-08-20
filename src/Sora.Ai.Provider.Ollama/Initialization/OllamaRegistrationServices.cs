using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.AI.Contracts.Routing;
using Sora.Ai.Provider.Ollama.Options;

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
                    var adapter = new OllamaAdapter(opt.Id, $"Ollama ({http.BaseAddress})", http, opt.DefaultModel);
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
            var envIsDev = Sora.Core.SoraEnv.IsDevelopment;
            var aiOpts = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.AI.Contracts.Options.AiOptions>>()?.Value;
            var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
            var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? false;
            if (!autoDiscovery) return Task.CompletedTask;
            if (!envIsDev && !allowNonDev) return Task.CompletedTask;

            foreach (var u in CollectCandidateUrls(_cfg))
            {
                try
                {
                    using var http = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromMilliseconds(400) };
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
                    var resp = http.GetAsync(Infrastructure.Constants.Discovery.TagsPath, cts.Token).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) continue;
                    var client = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromSeconds(60) };
                    var id = $"ollama@{u.Host}:{u.Port}";
                    _registry.Add(new OllamaAdapter(id, $"Ollama ({u})", client, defaultModel: null));
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
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url) { if (!string.IsNullOrWhiteSpace(url)) set.Add(url.Trim()); }
        var fromEnv = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
        Add(fromEnv ?? string.Empty);
        var multi = Environment.GetEnvironmentVariable("SORA_AI_OLLAMA_URLS");
        if (!string.IsNullOrWhiteSpace(multi))
        {
            foreach (var part in multi.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        Add($"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://127.0.0.1:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://host.docker.internal:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://ollama:{Infrastructure.Constants.Discovery.DefaultPort}");
        foreach (var s in set)
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri)) yield return uri;
        }
    }
}
