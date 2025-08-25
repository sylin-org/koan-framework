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
        services.AddOptions<WeaviateOptions>().BindConfiguration(Infrastructure.Constants.Configuration.Section).ValidateDataAnnotations();
        // Post-configure: if Endpoint is not explicitly provided (or left at default), try to self-configure
        services.PostConfigure<WeaviateOptions>(opts =>
        {
            try
            {
                // If Endpoint is not explicitly provided, left at default, or set to 'auto', try to self-configure
                if (string.IsNullOrWhiteSpace(opts.Endpoint) || IsDefault(opts.Endpoint) || IsAuto(opts.Endpoint))
                {
                    foreach (var url in CollectCandidateUrls())
                    {
                        if (Probe(url, TimeSpan.FromMilliseconds(250))) { opts.Endpoint = url; break; }
                    }
                }
            }
            catch { /* best-effort; remain with configured/default endpoint */ }
        });
        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Abstractions.Naming.INamingDefaultsProvider), typeof(WeaviateNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IVectorAdapterFactory, WeaviateVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, WeaviateHealthContributor>());
        services.AddHttpClient("weaviate");
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Configuration.Read(cfg, "Sora:Data:Weaviate:Endpoint", null) ?? "http://localhost:8085";
        report.AddSetting("Weaviate:Endpoint", endpoint, isSecret: false);
        // Discovery visibility
        report.AddSetting("Discovery:EnvList", Infrastructure.Constants.Discovery.EnvList, isSecret: false);
        report.AddSetting("Discovery:DefaultLocal", $"http://{Infrastructure.Constants.Discovery.Localhost}:{Infrastructure.Constants.Discovery.DefaultPort}", isSecret: false);
        report.AddSetting("Discovery:DefaultCompose", $"http://{Infrastructure.Constants.Discovery.WellKnownServiceName}:{Infrastructure.Constants.Discovery.DefaultPort}", isSecret: false);
    }

    private static bool IsDefault(string endpoint)
        => endpoint.TrimEnd('/') == "http://localhost:8085";

    // Host-first discovery similar to Ollama
    private static IEnumerable<string> CollectCandidateUrls()
    {
        // Ordered, de-duplicated list (env-list first, then host-first defaults)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void Add(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var u = url.Trim().TrimEnd('/');
            if (seen.Add(u)) ordered.Add(u);
        }

        // Read environment-driven list (comma/semicolon separated) for parity with Ollama
        var list = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(list))
        {
            foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }

        // Well-known defaults in strict host-first order
        Add($"http://{Infrastructure.Constants.Discovery.HostDocker}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.Localhost}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.WellKnownServiceName}:{Infrastructure.Constants.Discovery.DefaultPort}");
        Add($"http://{Infrastructure.Constants.Discovery.Localhost}:{Infrastructure.Constants.Discovery.LocalFallbackPort}");

        foreach (var u in ordered) yield return u;
    }

    private static bool IsAuto(string endpoint)
        => string.Equals(endpoint?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    private static bool Probe(string baseUrl, TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
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
