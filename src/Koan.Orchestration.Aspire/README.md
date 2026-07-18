# Koan.Orchestration.Aspire

Koan's .NET Aspire integration projects referenced Koan orchestration metadata into an Aspire
`DistributedApplicationBuilder`.

> **Maturity:** unassessed in the generated product surface. The source builds and focused discovery
> behavior exists, but there is no current graduated sample or public package-install/deployment
> guarantee. Use it from the source checkout when evaluating the integration.

## Current surface

```csharp
using Koan.Orchestration.Aspire.Extensions;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddKoanDiscoveredResources();
await builder.Build().RunAsync();
```

`AddKoanDiscoveredResources()` reads the application's orchestration manifest and contributes the
resources described by referenced modules. Aspire remains the owner of application topology,
resource lifetimes, endpoints, dashboards, and deployment tooling; Koan supplies metadata and
provider intent.

## Responsibility boundary

- Application and module projects declare orchestration metadata.
- Resource-contributing modules implement the inert contract from
  `Sylin.Koan.Orchestration.Aspire.Abstractions`; that reference does not activate this runtime.
- Koan's manifest generator compiles that metadata.
- This package translates the manifest into Aspire resource registrations.
- Aspire owns execution and orchestration behavior.

Reference = Intent does not mean every optional provider is healthy or selected. Provider readiness,
credentials, topology, and production posture remain explicit and must be verified for the target
application.

## Evidence and next step

Implementation details and the currently exercised boundary are in [`TECHNICAL.md`](TECHNICAL.md).
Before promoting this package, add one graduated Aspire sample that starts the actual resource graph,
observes Koan startup/facts, and proves clean shutdown and correction behavior.
