using Microsoft.Extensions.Configuration;
using Sora.AI.Contracts.Adapters;
using Sora.AI.Contracts.Routing;
using Sora.Core;
using Sora.Core.Observability.Health;

namespace Sora.Ai.Provider.Ollama.Health;

/// Health contributor that validates Ollama availability and required models.
internal sealed class OllamaHealthContributor : IHealthContributor
{
    private readonly IAiAdapterRegistry _registry;
    private readonly IConfiguration _cfg;
    public OllamaHealthContributor(IAiAdapterRegistry registry, IConfiguration cfg)
    { _registry = registry; _cfg = cfg; }

    public string Name => "ai:ollama";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var adapters = _registry.All.Where(a => string.Equals(a.Type, Infrastructure.Constants.Adapter.Type, StringComparison.OrdinalIgnoreCase)).ToList();
        if (adapters.Count == 0)
        {
            return new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Unhealthy, "no ollama adapters registered", null, null);
        }

        // Collect models across all reachable adapters with short per-call timeout
        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reachable = 0;
        foreach (var a in adapters)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                var list = await a.ListModelsAsync(cts.Token).ConfigureAwait(false);
                foreach (var m in list) if (!string.IsNullOrWhiteSpace(m.Name)) models.Add(m.Name);
                reachable++;
            }
            catch { /* ignore individual failures */ }
        }

        var data = new Dictionary<string, object?>
        {
            ["adapters"] = adapters.Count,
            ["reachable"] = reachable,
            ["models.count"] = models.Count,
            ["models"] = models.Take(16).ToArray()
        };

        if (reachable == 0)
        {
            return new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Unhealthy, "all ollama endpoints unreachable", null, data);
        }

        // Required models (optional): Sora:Ai:Ollama:RequiredModels: [ "all-minilm", ... ]
        var required = _cfg.GetSection("Sora:Ai:Ollama:RequiredModels").Get<string[]>() ?? Array.Empty<string>();
        var missing = required.Where(r => !models.Contains(r)).ToArray();
        if (missing.Length > 0)
        {
            data["required.missing"] = missing;
            return new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Unhealthy, $"missing models: {string.Join(", ", missing)}", null, data);
        }

    return new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Healthy, null, null, data);
    }
}
