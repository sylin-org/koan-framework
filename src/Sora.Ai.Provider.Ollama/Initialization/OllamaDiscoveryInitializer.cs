using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts.Options;
using Sora.AI.Contracts.Routing;
using Sora.Ai.Provider.Ollama.Infrastructure;

namespace Sora.Ai.Provider.Ollama.Initialization;

internal sealed class OllamaDiscoveryInitializer : Sora.Core.ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Resolve env/config to decide if discovery should run
        using var sp = services.BuildServiceProvider();
        var cfg = sp.GetService<IConfiguration>();
        var envIsDev = Sora.Core.SoraEnv.IsDevelopment;
        var aiOpts = sp.GetService<Microsoft.Extensions.Options.IOptions<AiOptions>>()?.Value;
        var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? (envIsDev);
        var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? false;
        if (!autoDiscovery) return;
        if (!envIsDev && !allowNonDev) return;

        // If registry missing, skip
        var reg = sp.GetService<IAiAdapterRegistry>();
        if (reg is null) return;

        var urls = CollectCandidateUrls(cfg);
        var discovered = new List<Uri>();
        foreach (var u in urls)
        {
            try
            {
                using var http = new HttpClient { BaseAddress = u, Timeout = TimeSpan.FromMilliseconds(400) };
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
                var resp = http.GetAsync(Constants.Discovery.TagsPath, cts.Token).GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode)
                    discovered.Add(u);
            }
            catch { /* ignore failed probe */ }
        }

        foreach (var baseUrl in discovered)
        {
            var id = $"ollama@{baseUrl.Host}:{baseUrl.Port}";
            // Register an HttpClient and adapter, then add to registry
            var clientName = $"sora.ai.ollama:disc:{id}";
            services.AddHttpClient(clientName, c => { c.BaseAddress = baseUrl; c.Timeout = TimeSpan.FromSeconds(60); });
            services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Core.ISoraInitializer>(new RegisterAdapterInitializer(id, clientName, defaultModel: null)));
        }
    }

    private static IEnumerable<Uri> CollectCandidateUrls(IConfiguration? cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url) { if (!string.IsNullOrWhiteSpace(url)) set.Add(url.Trim()); }

        // Env and config hints
        var fromEnv = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
        Add(fromEnv ?? string.Empty);
        var multi = Environment.GetEnvironmentVariable("SORA_AI_OLLAMA_URLS");
        if (!string.IsNullOrWhiteSpace(multi))
        {
            foreach (var part in multi.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }

        // Common hostnames
        Add($"http://localhost:{Constants.Discovery.DefaultPort}");
        Add($"http://127.0.0.1:{Constants.Discovery.DefaultPort}");
        Add($"http://host.docker.internal:{Constants.Discovery.DefaultPort}");
        Add($"http://ollama:{Constants.Discovery.DefaultPort}");

        foreach (var s in set)
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri)) yield return uri;
        }
    }
}
