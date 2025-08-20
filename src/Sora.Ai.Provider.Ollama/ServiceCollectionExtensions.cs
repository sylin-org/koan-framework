using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts.Routing;
using Sora.Ai.Provider.Ollama.Infrastructure;
using Sora.Ai.Provider.Ollama.Options;

namespace Sora.Ai.Provider.Ollama;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaFromConfig(this IServiceCollection services)
    {
        services.AddOptions<OllamaServiceOptions[]>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Core.ISoraInitializer>(sp => new OllamaInitializer()));
        return services;
    }

    private sealed class OllamaInitializer : Sora.Core.ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Build a temporary provider to access configuration and IAiAdapterRegistry
            using var sp = services.BuildServiceProvider();
            var cfg = sp.GetService<IConfiguration>();
            var reg = sp.GetService<IAiAdapterRegistry>();
            if (cfg is null || reg is null) return; // core not wired yet

            var section = cfg.GetSection(Constants.Configuration.ServicesRoot);
            var items = section.Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();
            var enabled = items.Where(i => i.Enabled).ToList();
            foreach (var opt in enabled)
            {
                // Configure a named HttpClient per adapter
                services.AddHttpClient($"sora.ai.ollama:{opt.Id}", c =>
                {
                    c.BaseAddress = new Uri(opt.BaseUrl);
                    c.Timeout = TimeSpan.FromSeconds(60);
                });
                // Register the adapter as a singleton with its HttpClient
                services.TryAddSingleton(sp2 =>
                {
                    var http = sp2.GetRequiredService<IHttpClientFactory>().CreateClient($"sora.ai.ollama:{opt.Id}");
                    return new OllamaAdapter(opt.Id, $"Ollama ({opt.BaseUrl})", http, opt.DefaultModel);
                });
                services.PostConfigure<Sora.AI.Contracts.Options.AiOptions>(o => { });
                // Defer actual registry Add until runtime provider exists
                services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Core.ISoraInitializer>(
                    new RegisterAdapterInitializer(opt.Id)));
            }
        }
    }

    private sealed class RegisterAdapterInitializer : Sora.Core.ISoraInitializer
    {
        private readonly string _id;
        public RegisterAdapterInitializer(string id) => _id = id;
        public void Initialize(IServiceCollection services)
        {
            // At this point we can resolve the named adapter instance and register it
            using var sp = services.BuildServiceProvider();
            var reg = sp.GetService<IAiAdapterRegistry>();
            var adapter = sp.GetService<OllamaAdapter>();
            if (reg is not null && adapter is not null)
                reg.Add(adapter);
        }
    }
}
