using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Weaviate.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Weaviate";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<WeaviateOptions>().BindConfiguration("Sora:Data:Weaviate").ValidateDataAnnotations();
        // Post-configure: if Endpoint is not explicitly provided (or left at default), try to self-configure
        services.PostConfigure<WeaviateOptions>(opts =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(opts.Endpoint) || IsDefault(opts.Endpoint))
                {
                    foreach (var url in CollectCandidateUrls())
                    {
                        if (Probe(url, TimeSpan.FromMilliseconds(250))) { opts.Endpoint = url; break; }
                    }
                }
            }
            catch { /* best-effort; remain with configured/default endpoint */ }
        });
        services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Sora.Data.Abstractions.Naming.INamingDefaultsProvider), typeof(WeaviateNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<Sora.Data.Vector.Abstractions.IVectorAdapterFactory, WeaviateVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, WeaviateHealthContributor>());
        services.AddHttpClient("weaviate");
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Sora.Core.Configuration.Read(cfg, "Sora:Data:Weaviate:Endpoint", null) ?? "http://localhost:8085";
        report.AddSetting("Weaviate:Endpoint", endpoint, isSecret: false);
    }

    private static bool IsDefault(string endpoint)
        => endpoint.TrimEnd('/') == "http://localhost:8085";

    // Host-first discovery similar to Ollama
    private static IEnumerable<string> CollectCandidateUrls()
    {
        // Ordered, de-duplicated list
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] candidates = new[]
        {
            // Host machine mapped ports (dev compose)
            "http://host.docker.internal:8080",
            "http://localhost:8080",
            // In-container default compose network name
            "http://weaviate:8080",
            // Conventional local default
            "http://localhost:8085"
        };
        foreach (var c in candidates)
        {
            var u = c.TrimEnd('/');
            if (seen.Add(u)) yield return u;
        }
    }

    private static bool Probe(string baseUrl, TimeSpan timeout)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = timeout };
            // Prefer well-known readiness endpoint (no version prefix in some Weaviate releases)
            var readyUrl = baseUrl + "/.well-known/ready";
            System.Diagnostics.Debug.WriteLine($"[Sora.Data.Weaviate] Probe GET {readyUrl}");
            var ready = http.GetAsync(readyUrl).GetAwaiter().GetResult();
            if ((int)ready.StatusCode == 200) return true;
            // Fallback for deployments exposing readiness under /v1
            var readyV1Url = baseUrl + "/v1/.well-known/ready";
            System.Diagnostics.Debug.WriteLine($"[Sora.Data.Weaviate] Probe fallback GET {readyV1Url} (prior {(int)ready.StatusCode})");
            var readyV1 = http.GetAsync(readyV1Url).GetAwaiter().GetResult();
            if ((int)readyV1.StatusCode == 200) return true;
            var schemaUrl = baseUrl + "/v1/schema";
            System.Diagnostics.Debug.WriteLine($"[Sora.Data.Weaviate] Probe schema GET {schemaUrl} (prior {(int)readyV1.StatusCode})");
            var schema = http.GetAsync(schemaUrl).GetAwaiter().GetResult();
            return schema.IsSuccessStatusCode || (int)schema.StatusCode == 405; // 405 on POST-only clusters still implies reachability
        }
        catch { return false; }
    }
}
