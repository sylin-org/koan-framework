using Microsoft.Extensions.Configuration;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Ai.Provider.Ollama.Health;

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
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, "no ollama adapters registered", null, null);
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
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, "all ollama endpoints unreachable", null, data);
        }

        // Required models (optional): Koan:Ai:Ollama:RequiredModels: [ "all-minilm", ... ]
        var required = _cfg.GetSection("Koan:Ai:Ollama:RequiredModels").Get<string[]>() ?? Array.Empty<string>();

        // Debug logging
        if (required.Length > 0)
        {
            var modelsList = string.Join(", ", models.Take(10).Select(m => $"'{m}'"));
            data["debug.available_models"] = modelsList;
            data["debug.required_models"] = string.Join(", ", required.Select(r => $"'{r}'"));
        }

        var missing = required.Where(r => !ModelExists(models, r)).ToArray();
        if (missing.Length > 0)
        {
            data["required.missing"] = missing;
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, $"missing models: {string.Join(", ", missing)}", null, data);
        }

        return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, data);
    }

    /// <summary>
    /// Check if a model exists in the available models, handling tag variations.
    /// Matches "all-minilm" against both "all-minilm" and "all-minilm:latest".
    /// </summary>
    private static bool ModelExists(HashSet<string> availableModels, string requiredModel)
    {
        if (string.IsNullOrEmpty(requiredModel)) return false;

        return availableModels.Any(available =>
        {
            // Exact match
            if (string.Equals(available, requiredModel, StringComparison.OrdinalIgnoreCase))
                return true;

            // Base name match (handle tags like :latest)
            var baseName = available.Split(':')[0];
            return string.Equals(baseName, requiredModel, StringComparison.OrdinalIgnoreCase);
        });
    }
}
