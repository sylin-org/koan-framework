using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Routing;
using Koan.Core.Adapters;

namespace Koan.AI.Connector.Ollama.Initialization;

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
                    var logger = _sp.GetService<ILogger<OllamaAdapter>>() ?? NullLogger<OllamaAdapter>.Instance;

                    // Create minimal configuration for the adapter
                    var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
                    var adapterConfig = configBuilder.Build();

                    var readinessDefaults = _sp.GetService<IOptions<AdaptersReadinessOptions>>()?.Value;
                    var adapter = new OllamaAdapter(http, logger, adapterConfig, readinessDefaults);
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
