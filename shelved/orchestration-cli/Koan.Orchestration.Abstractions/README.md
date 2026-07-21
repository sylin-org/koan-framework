# Sylin.Koan.Orchestration.Abstractions

Inert contracts for Koan's development-host tooling: hosting providers, artifact exporters, plans,
profiles, and legacy manifest vocabulary.

## Install

```powershell
dotnet add package Sylin.Koan.Orchestration.Abstractions
```

Most application developers should not reference this package. Choose it when implementing a DevHost
engine or artifact renderer that must plug into `Sylin.Koan.Orchestration.Cli` without activating the
CLI, Docker, Podman, Compose export, or an application runtime capability.

## Smallest meaningful use

Implement one explicit tooling role:

```csharp
using Koan.Orchestration.Abstractions;

public sealed class AcmeHostingProvider : IHostingProvider
{
    public string Id => "acme";
    public int Priority => 100;

    // Implement availability, lifecycle, logs, status, and live-port inspection.
}
```

`IArtifactExporter` is the corresponding contract for rendering a `Plan` into a development artifact.
The CLI elects implementations by their standard interface and priority; this package performs no
discovery or registration by itself.

## Boundaries

- Runtime service identity and discovery defaults live in `Sylin.Koan.Core` under
  `Koan.Core.Services`; application Core does not depend on this DevHost package.
- This package does not start containers, generate Compose files, inspect engines, or modify an app.
- Docker and Podman implementations ship separately. The Compose renderer also ships separately.
- The remaining service/app manifest attributes support current DevHost inspection. They are tooling
  vocabulary, not application capability activation.

See [TECHNICAL.md](./TECHNICAL.md) for the complete SPI and model ownership.
