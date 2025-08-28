---
title: Orchestration Manifest Generator — unified attributes and manifest-first discovery
description: How to declare service/app metadata with SoraService/SoraApp so Sora generates a single manifest consumed by the CLI and planners
---

# Orchestration Manifest Generator — unified attributes and manifest-first discovery

Contract (at a glance)
- Inputs: annotated types in referenced assemblies: [SoraService] on adapter types, optional [SoraApp] (or ISoraManifest) app anchor.
- Outputs: one embedded JSON manifest (__SoraOrchestrationManifest.Json) with schemaVersion, services[], app{}, authProviders[].
- Error modes: invalid shortCode/qualifiedCode, duplicate shortCode within a compilation, missing image when DeploymentKind=Container, latest tag warnings. The generator reports diagnostics; the build stays green.
- Success: CLI/Planner use only the generated manifest (no heuristics). `sora inspect` shows unified fields (kind, codes, image/tag, ports, provides/consumes, capabilities).

## Authoring: add attributes

Use the unified attribute on each adapter or service provider. Prefer stable short codes and qualified codes.

```csharp
using Sora.Orchestration.Attributes;

[SoraService(
    kind: ServiceKind.Database,
    shortCode: "postgres",
    name: "PostgreSQL",
    QualifiedCode = "sora.db.relational.postgres",
    Description = "Relational database",
    DeploymentKind = DeploymentKind.Container,
    ContainerImage = "postgres",
    DefaultTag = "16",
    DefaultPorts = new[] { 5432 },
    HealthEndpoint = null,
    Provides = new[] { "db:postgres" },
    Consumes = null,
    // key=value strings → become a capabilities map in the manifest
    Capabilities = new[] { "protocol=postgres", "ssl=supported" }
)]
public sealed class PostgresAdapter /* : IServiceAdapter */ { }
```

Declare an app anchor once per application to surface the app block and default public port. Either implement `ISoraManifest` or add the attribute directly to a trivial type.

```csharp
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

[SoraApp(AppCode = "api", AppName = "S2 API", Description = "Sample API", DefaultPublicPort = 8080)]
public sealed class AppManifest : ISoraManifest { }
```

Notes
- Capabilities are normalized as a map from an array of `"key=value"` strings.
- `kind` uses the closed enum ServiceKind (App, Database, Vector, Ai, …). `Subtype` is an optional free-form taxonomy string.
- Provide `ContainerImage` when `DeploymentKind=Container`; the generator warns if it’s missing.
- Use `Provides`/`Consumes` to let the planner compute dependencies; avoid implicit image-name or port heuristics.

## What the generator emits

At build time, the generator scans referenced assemblies, validates attributes, and emits a single internal type with an embedded JSON payload:

```csharp
namespace Sora.Orchestration
{
    internal static class __SoraOrchestrationManifest
    {
        public const string Json = "{\"schemaVersion\":1,\"services\":[...],\"app\":{...},\"authProviders\":[...]}";
    }
}
```

Important fields (per service)
- kind (int enum), shortCode, name, qualifiedCode, description
- deploymentKind, containerImage, defaultTag, defaultPorts
- healthEndpoint (http-only), provides[], consumes[]
- capabilities { key: value }

App block
- appCode, appName, description, defaultPublicPort

Diagnostics (subset)
- Short code invalid/reserved
- Duplicate short code within a compilation
- Qualified code format issues
- DeploymentKind=Container but image missing
- Image tag set to latest (information)

## Discovery and planning

Manifest-first and only. The CLI doesn’t scrape compose/csproj/image names and doesn’t reflect for legacy attributes.

Flow
1) CLI loads `__SoraOrchestrationManifest.Json` from built assemblies in the working set.
2) Inspect/Planner read unified fields, compute `depends_on` via `Provides`/`Consumes`.
3) Profiles and overrides can adjust image/tag/ports during planning (profile-aware merges).
4) App defaults to a deterministic port when not specified; assignment is persisted in `.sora/manifest.json`.

Inspect extras
- Surfaces duplicate service ids across manifests (JSON: `duplicates`).
- Shows app ids and assigned public port.
- Unified fields are included in JSON and summarized in human output.

## Minimal example (schema excerpt)

```json
{
  "schemaVersion": 1,
  "app": { "appCode": "api", "appName": "S2 API", "defaultPublicPort": 8080 },
  "services": [
    {
      "shortCode": "postgres",
      "name": "PostgreSQL",
      "kind": 1,
      "qualifiedCode": "sora.db.relational.postgres",
      "deploymentKind": 1,
      "containerImage": "postgres",
      "defaultTag": "16",
      "defaultPorts": [5432],
      "provides": ["db:postgres"],
      "capabilities": { "protocol": "postgres", "ssl": "supported" }
    }
  ]
}
```

## Troubleshooting (quick)
- “No services discovered”: ensure your adapters reference `Sora.Orchestration.Abstractions` and are attributed with `[SoraService]`. Build the solution and re-run `sora inspect`.
- “Missing image warning”: set `ContainerImage` or provide `[ContainerDefaults]` on the class if your adapter also supports legacy defaults.
- “Duplicate ids”: fix shortCode collisions across referenced manifests; `sora inspect` prints a DUPLICATE IDS section.

## References
- Decision — ARCH-0049 Unified service metadata and discovery: ../decisions/ARCH-0049-unified-service-metadata-and-discovery.md
- Engineering — Front door: ./index.md
- Reference — Orchestration: ../reference/orchestration.md
