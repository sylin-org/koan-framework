using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts.Routing;
using Sora.Ai.Provider.Ollama.Infrastructure;
using Sora.Ai.Provider.Ollama.Options;

namespace Sora.Ai.Provider.Ollama.Initialization;

internal sealed class OllamaConfigInitializer : Sora.Core.ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Build a temporary provider to access configuration and IAiAdapterRegistry
        using var sp = services.BuildServiceProvider();
        var cfg = sp.GetService<IConfiguration>();
        var reg = sp.GetService<IAiAdapterRegistry>();
        if (cfg is null || reg is null) return; // AI core not wired yet

        var items = cfg.GetSection(Constants.Configuration.ServicesRoot).Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();
        foreach (var opt in items.Where(i => i.Enabled))
        {
            var clientName = $"sora.ai.ollama:{opt.Id}";
            services.AddHttpClient(clientName, c =>
            {
                c.BaseAddress = new Uri(opt.BaseUrl);
                c.Timeout = TimeSpan.FromSeconds(60);
            });
            services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Core.ISoraInitializer>(new RegisterAdapterInitializer(opt.Id, clientName, opt.DefaultModel)));
        }
    }
}

internal sealed class RegisterAdapterInitializer : Sora.Core.ISoraInitializer
{
    private readonly string _id;
    private readonly string _clientName;
    private readonly string? _defaultModel;
    public RegisterAdapterInitializer(string id, string clientName, string? defaultModel)
    { _id = id; _clientName = clientName; _defaultModel = defaultModel; }

    public void Initialize(IServiceCollection services)
    {
        using var sp = services.BuildServiceProvider();
        var reg = sp.GetService<IAiAdapterRegistry>();
        var httpFactory = sp.GetService<System.Net.Http.IHttpClientFactory>();
        if (reg is null || httpFactory is null) return;
        var http = httpFactory.CreateClient(_clientName);
        var adapter = new OllamaAdapter(_id, $"Ollama ({http.BaseAddress})", http, _defaultModel);
        reg.Add(adapter);
    }
}
