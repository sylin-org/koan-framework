using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Recipe.Abstractions;

[assembly: SoraRecipe(typeof(Sora.Recipe.Observability.ObservabilityRecipe))]

namespace Sora.Recipe.Observability;

public sealed class ObservabilityRecipe : ISoraRecipe
{
    public string Name => "observability";
    public int Order => 100; // Observability lane

    public bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true;

    public void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Health checks baseline (if HealthChecks is available via Sora.Web)
        try
        {
            services.AddHealthChecks();
        }
        catch { /* AddHealthChecks unavailable; ignore */ }

        // Simple resilient HttpClient for outbound probes (example)
        services.AddHttpClient("sora-observability")
            .AddStandardResilienceHandler(); // .NET 9 built-in resilience handler

        // Future: add OTEL via existing Sora diagnostics extension when available
    }
}
