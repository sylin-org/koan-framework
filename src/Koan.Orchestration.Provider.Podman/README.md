# Koan.Orchestration.Provider.Podman

## Contract
- **Purpose**: Extend Koan orchestration to Podman environments, mirroring Docker features with pod-native configuration.
- **Primary inputs**: Podman engine connection options, Koan adapter descriptors, and module metadata.
- **Outputs**: Podman-specific orchestration descriptors, Compose-compatible manifests, and boot diagnostics capturing provider state.
- **Failure modes**: Podman socket not reachable, missing Podman Compose features, or containers requiring privileges unavailable in Podman.
- **Success criteria**: Generated manifests run under Podman, adapters receive accurate pod metadata, and planners detect Podman capabilities correctly.

## Quick start
```csharp
public sealed class PodmanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Podman";

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanAdapter<PodmanOrchestrationAdapter>();
        services.Configure<PodmanOptions>(options =>
        {
            options.Uri = "unix:///run/podman/podman.sock";
            options.GenerateKubeSpecs = true;
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Podman orchestration provider registered");
}
```
- Register the Podman adapter to surface Podman-specific features (pods, kube manifests, rootless support).
- Compose renderers reuse metadata emitted by the provider, so Docker/Podman parity requires no extra wiring.

## Configuration
- `PodmanOptions.Uri`: socket or TCP endpoint.
- `PodmanOptions.GenerateKubeSpecs`: enable extra Kubernetes manifest exports.
- Provide registry credentials via Koan secrets to support private image pulls.

## Edge cases
- Rootless users: ensure Podman socket permissions allow the Koan process to connect.
- SELinux contexts: configure volume labels in adapter metadata when targeting Podman on SELinux-enabled systems.
- Compose gaps: Podman Compose lags Docker Compose features; document provider capability differences using `AdapterCapabilities`.
- Remote Podman: secure the connection with TLS if running Podman machine.

## Related packages
- `Koan.Orchestration.Abstractions` – orchestration planner consumed here.
- `Koan.Orchestration.Renderers.Compose` – uses Podman metadata when rendering Compose.
- `Koan.Orchestration.Provider.Docker` – sibling provider for Docker parity testing.

## Reference
- `PodmanOrchestrationAdapter` – implementation bridging Podman APIs.
- `PodmanOptions` – typed configuration used to connect to the engine.
- `AdapterCapabilities` – declare per-engine feature support.
