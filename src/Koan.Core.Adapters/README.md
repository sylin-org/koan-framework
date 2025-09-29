# Koan.Core.Adapters

> ✅ Validated against `BaseKoanAdapter` and readiness services on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for the deep dive.

## Contract
- **Purpose**: Provide the unified adapter foundation for Koan modules (storage, messaging, orchestration) with capability negotiation and bootstrap reporting.
- **Primary inputs**: Implementations of `BaseKoanAdapter`, adapters registered through `IKoanAdapter`, configuration snapshots, and capability manifests.
- **Outputs**: Adapter registration with Koan auto-registrars, capability metadata surfaced via `AdapterCapabilities`, and readiness diagnostics.
- **Failure modes**: Missing capability declarations, adapters not registered through `KoanAutoRegistrar`, or template scaffolds left unimplemented.
- **Success criteria**: Adapters self-describe capabilities, integrate with orchestration bridges, and participate in readiness/reporting pipelines out of the box.

## Quick start
```csharp
using Koan.Core.Adapters;
using Koan.Core;

public sealed class MySearchAdapter : BaseKoanAdapter
{
    public override string Name => "search";

    protected override void Describe(AdapterCapabilities caps)
    {
        caps.WithCategory("search")
            .Supports("index:create")
            .Supports("query:vector");
    }
}

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Search";

    public void Initialize(IServiceCollection services)
        => services.AddKoanAdapter<MySearchAdapter>();

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Search adapter registered");
}
```
- Derive from `BaseKoanAdapter` to get capability reporting, orchestration hooks, and configuration helpers.
- Register adapters via `IKoanAutoRegistrar` so they participate in Koan’s bootstrap discovery without manual program wiring.

## Configuration
- Provide strongly-typed options by calling `ConfigureOptions<TOptions>()` inside your adapter.
- Use `MissingTypes` helpers to validate required services and emit actionable error messages.
- Surface readiness information by overriding `BuildReadiness` and contributing data through `ReadinessReport`.

## Edge cases
- Multiple adapters with the same name will override one another; use unique identifiers per module.
- Long-running readiness checks should stream using `ReadinessProbeContext.WriteAsync(...)` instead of blocking.
- If an adapter depends on optional assemblies, wrap reflection calls with `MissingTypes.ThrowIfMissing` to guard against trimming.
- Adapters running outside orchestration contexts can bypass `OrchestrationRuntimeBridge`; ensure your code checks `IsOrchestrationAware` first.

## Related packages
- `Koan.Core` – shared configuration/environment facilities used for adapter discovery.
- `Koan.Orchestration.Abstractions` – orchestration bridge interfaces consumed by adapter scaffolding.
- `Koan.Data.Abstractions` – provides base entity contracts for data adapters.

## Documentation
- [`TECHNICAL.md`](./TECHNICAL.md) – lifecycle, readiness pipeline, and capability DSL reference.

## Reference
- `BaseKoanAdapter` – base class implementing capability negotiation.
- `AdapterCapabilities` – fluent DSL for declaring support matrix.
- `BootstrapReport` – adapter reporting surface.
