using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Recipe.Abstractions;

[assembly: KoanRecipe(typeof(Koan.Recipe.Observability.ObservabilityRecipe))]

namespace Koan.Recipe.Observability;

public sealed class ObservabilityRecipe : IKoanRecipe
{
    public string Name => "observability";
    public int Order => 100; // Observability lane

    public bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true;

    public void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Health checks baseline (if HealthChecks is available via Koan.Web)
        try
        {
            services.AddHealthChecks();
        }
        catch { /* AddHealthChecks unavailable; ignore */ }

        // Simple resilient HttpClient for outbound probes (example)
        services.AddHttpClient("Koan-observability")
            .AddStandardResilienceHandler(); // .NET 9 built-in resilience handler

        // Future: add OTEL via existing Koan diagnostics extension when available
    }
}
