using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Ai.Provider.Ollama.Options;
using Koan.AI.Contracts.Routing;

namespace Koan.Ai.Provider.Ollama.Initialization;

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