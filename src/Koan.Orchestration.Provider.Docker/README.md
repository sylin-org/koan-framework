# Koan.Orchestration.Provider.Docker

## Contract
- **Purpose**: Integrate Docker container metadata into Koan orchestration, powering Compose export and container health probing.
- **Primary inputs**: Adapter registration via `IKoanAutoRegistrar`, Docker engine connection settings, module descriptors emitted by Koan adapters.
- **Outputs**: Docker-specific orchestration descriptors, health/readiness notes in the boot report, and Compose artifacts assembled by Koan orchestration renderers.
- **Failure modes**: Docker daemon unreachable, missing permissions to query containers, or adapters not advertising container capabilities.
- **Success criteria**: Modules targeting Docker receive accurate service definitions, Compose files include correct mounts and ports, and orchestration diagnostics surface container issues.

## Quick start
```csharp
public sealed class DockerAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Docker";

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanAdapter<DockerOrchestrationAdapter>();
        services.Configure<DockerOptions>(options =>
        {
            options.Host = "unix:///var/run/docker.sock";
            options.DefaultNetwork = "koan-dev";
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Docker orchestration provider registered");
}
```
- Register the Docker adapter to expose container metadata; configure host/network defaults through typed options.
- When rendering Compose bundles, call `OrchestrationPlanner.PlanAsync()`; the Docker provider contributes container images, environment variables, and volume mounts.

## Configuration
- `DockerOptions.Host`: socket or TCP endpoint for the Docker engine.
- `DockerOptions.Networks`: custom networks to create or reuse during Compose generation.
- Provide credentials (if remote) via Koan secrets integration.

## Edge cases
- Rootless Docker: ensure socket path is accessible to the Koan process; adjust `DockerOptions.Host` accordingly.
- Windows containers: confirm Compose renderer selects the correct isolation; supply explicit `Platform` hints via adapter metadata.
- Rate limit on Docker API: throttle discovery calls or cache responses between Compose renders.
- SELinux/AppArmor mount restrictions: declare mounts explicitly and document policies in adapter capability notes.

## Related packages
- `Koan.Orchestration.Abstractions` – provides the planner and descriptor models.
- `Koan.Orchestration.Renderers.Compose` – consumes Docker metadata to render Compose files.
- `Koan.Orchestration.Provider.Podman` – sibling provider for Podman engines.

## Reference
- `DockerOrchestrationAdapter` – adapter implementation surfacing container metadata.
- `DockerOptions` – configuration object for engine connectivity.
- `OrchestrationRuntimeBridge` – orchestrator integration used by the provider.
