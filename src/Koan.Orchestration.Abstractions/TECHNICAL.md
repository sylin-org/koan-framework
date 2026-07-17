# Sylin.Koan.Orchestration.Abstractions — technical contract

## Responsibility

This package is the dependency-light SPI between DevHost orchestration mechanics and their optional
implementations. It owns vocabulary only; functional packages own registration, election, execution,
and operator-facing commands.

## Hosting provider SPI

`IHostingProvider` describes one engine implementation:

- `Id` is its stable operator-facing identity;
- `Priority` participates in CLI-owned election;
- `IsAvailable` probes whether the engine can be used without mutating it;
- `Up`, `Down`, `Logs`, `Status`, and `LivePorts` execute the selected engine behavior;
- `EngineInfo` returns inspectable engine metadata.

Provider implementations must honor cancellation, avoid logging secrets, and return corrective
availability reasons. Merely referencing this abstractions package never elects a provider.

## Artifact exporter SPI

`IArtifactExporter` converts the canonical `Plan` into an artifact format. `Supports` and
`ExporterCapabilities` describe the implementation; `Generate` owns the actual output. Exporters do
not own project discovery or planning.

## Models

- `Plan`, `ServiceSpec`, and `HealthSpec` are the canonical handoff from planning to exporters.
- `Profile` and the DevHost-specific options describe development execution posture.
- `RunOptions`, `StopOptions`, `LogsOptions`, `StatusOptions`, `ProviderStatus`, `PortBinding`, and
  `EngineInfo` form the hosting-provider command boundary.
- `PlanDraft` and `ServiceRequirement` support CLI planning before a final immutable plan is emitted.

Runtime service description is deliberately not owned here. `Koan.Core.Services.KoanServiceAttribute`,
`ServiceKind`, and `DeploymentKind` ship in `Sylin.Koan.Core`, where application discovery can consume
them without pulling a development-host SPI into every Koan app. The orchestration source generator
observes that contract by metadata name.

## Legacy manifest vocabulary

The attributes under `Koan.Orchestration.Attributes` remain input vocabulary for the current CLI and
generator where they are still used. New service-backed adapters use the Core `KoanServiceAttribute`.
Do not use these attributes as an application activation mechanism; Reference = Intent activation is
owned by functional `KoanModule` implementations.

## Failure and security boundaries

- Availability probes must be read-only.
- Engine and exporter errors should preserve the actionable operation and bounded public identity;
  credentials, raw environment values, and unredacted command output must not cross the SPI.
- `Redaction` is retained for DevHost consumers, but application runtime redaction is owned by
  `Koan.Core.Redaction`.
- This contract does not promise container availability, artifact correctness, or deployment support;
  those claims belong to the selected implementation and its executable evidence.

## Related packages

- `Sylin.Koan.Orchestration.Cli.Core`: planning and command runtime.
- `Sylin.Koan.Orchestration.Cli`: operator executable.
- `Sylin.Koan.Orchestration.Connector.Docker` and `.Podman`: hosting providers.
- `Sylin.Koan.Orchestration.Renderers.Connector.Compose`: Compose artifact exporter.
- `Sylin.Koan.Orchestration.Generators`: build-time manifest observation.
