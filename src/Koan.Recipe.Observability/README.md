# Koan.Recipe.Observability

## Contract
- **Purpose**: Ship a prebuilt observability bundle for Koan applications, wiring health checks, OpenTelemetry, and resilient HTTP clients.
- **Primary inputs**: `ObservabilityRecipe` implementation, Koan recipe registry, environment configuration for telemetry exporters.
- **Outputs**: Registered OpenTelemetry pipelines, health probe endpoints, and HTTP client policies.
- **Failure modes**: Missing exporter configuration (OTLP endpoint), disabled health probe dependencies, or recipe applied in an unsupported environment.
- **Success criteria**: Applications emit traces/metrics with sane defaults, health checks expose readiness endpoints, and HttpClient resiliency policies are active.

## Quick start
```csharp
public sealed class ObservabilityAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Observability";

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanRecipe<ObservabilityRecipe>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Observability recipe registered");
}

// During startup
await RecipeApplier.ApplyAsync("observability:otel", cancellationToken);
```
- Register the recipe so it becomes available to the recipe applier; optionally auto-apply it during bootstrap.
- Override options via configuration (e.g., `Koan:Telemetry:Otlp:Endpoint`) to point at your collector.

## Configuration
- OTLP exporter: set endpoint, headers, and resource attributes through standard Koan configuration sources.
- Health probes: customize tags and intervals in the recipe options.
- HttpClient policies: configure retry/backoff counts by binding `Koan:Http:Policies`.

## Edge cases
- Exporter offline: recipe still configures instrumentation, but OpenTelemetry fallback logs warnings—ensure collectors are reachable.
- Local development: enable console exporter for easy debugging by toggling `Koan:Telemetry:Console`.
- High-throughput services: adjust batch size and flush intervals to prevent blocking.
- Restricted environments: disable tracing selectively via feature flags if telemetry is disallowed.

## Related packages
- `Koan.Recipe.Abstractions` – recipe infrastructure consumed here.
- `Koan.Core` – options and logging helpers for telemetry settings.
- `Koan.Web` – health endpoint hosting.

## Reference
- `ObservabilityRecipe` – implementation applying telemetry services.
- `RecipeApplier` – API for enabling recipes at runtime.
- `/docs/guides/observability/index.md` – extend with specifics for this recipe.
