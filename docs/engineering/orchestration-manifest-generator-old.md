---
title: Orchestration manifest generator — attribute-driven service metadata
description: How Sora developers declare orchestration metadata via attributes and let the Roslyn generator produce a manifest consumed by the CLI/analyzer.
---

# Orchestration manifest generator — attribute-driven service metadata

Contract (at a glance)
- Inputs: C# attributes on a concrete type in your project/adapter: ServiceId, ContainerDefaults, EndpointDefaults, AppEnvDefaults, HealthEndpointDefaults.
- Output: a generated in-assembly manifest (`__SoraOrchestrationManifest.Json`) listing services with image, ports, env, volumes, default endpoints (container/local), and optional HTTP health settings.
- Error modes: missing `ServiceId` or `ContainerDefaults.Image` → candidate ignored; invalid attribute values → generator skips that piece and continues; generator failures are swallowed to not break builds.
- Success: the CLI discovers your service deterministically (generator-first), exports Compose with correct ports/env/volumes, emits healthchecks only for HTTP endpoints, and chooses depends_on accordingly.

Scope and precedence
- Generator-first discovery: the CLI/analyzer prefers the generated `__SoraOrchestrationManifest.Json` when present.
- Fallbacks (in order): descriptor file → generated manifest → legacy reflection (attributes read at runtime) → demo heuristics as last resort.
- Health is HTTP-only: only services with `EndpointDefaults` that yield `http` or `https` and a `HealthEndpointDefaults` path get a healthcheck.

## How it works

During build, the Roslyn source generator scans class declarations for orchestration attributes and collects candidates into a JSON manifest. It then emits a generated file `__SoraOrchestrationManifest.g.cs` that contains:

```csharp
namespace Sora.Orchestration { public static class __SoraOrchestrationManifest { public const string Json = "..."; } }
```

The CLI/analyzer reads this JSON at runtime to build a Plan. No files are written to disk; the manifest travels embedded in the assembly.

Repo convenience: in this repository, the generator is auto-attached to all projects via `Directory.Build.props` as an Analyzer reference, so you don’t need to add a package reference explicitly.

## Attributes to use

- ServiceIdAttribute(id)
  - Required. Stable service id (e.g., "mongo", "api").

- ContainerDefaultsAttribute(image)
  - Required. Container image name (e.g., "mongo").
  - Named args: `Tag` (e.g., "7"), `Ports` (params int[]), `Env` (params "KEY=VALUE"), `Volumes` (params "host:container" or just container path if you prefer HostMount discovery elsewhere).

- EndpointDefaultsAttribute(mode, scheme, host, port, UriPattern = null)
  - Recommended. Provide at least one for `EndpointMode.Container`. Optionally another for `EndpointMode.Local`.
  - For HTTP/S services, set `scheme` to "http" or "https"; `host` is the container-default host (often the service name); `port` is the container port.
  - `UriPattern` overrides the rendered hint (e.g., "postgres://{host}:{port}").

- AppEnvDefaultsAttribute(params string[] keyEqualsValue)
  - Optional. Application-facing env pairs that the exporter can pass through and that the app can bind to options (e.g., `Sora__Data__Mongo__ConnectionString=...`).

- HealthEndpointDefaultsAttribute(httpPath, IntervalSeconds=15, TimeoutSeconds=2, Retries=5)
  - Optional (HTTP-only). Enables a compose healthcheck via curl when the endpoint scheme is http/https.
  - Use a lightweight path like "/health" or "/v1/.well-known/ready".

Notes
- Prefer putting these attributes on your adapter/service factory class (non-abstract) that best represents the service.
- The generator ignores abstract types and types missing required metadata.

## JSON shape (generated)

Each service entry may include:
- id, image, ports[], env{ key: value }, volumes[], appEnv{ key: value }
- scheme, host, endpointPort, uriPattern
- localScheme, localHost, localPort, localPattern (when a Local endpoint is provided)
- healthPath, healthInterval, healthTimeout, healthRetries (when HTTP health is declared)

You don’t write this JSON; it’s produced from your attributes. It’s provided here so you know what is captured.

## Minimal examples

Postgres adapter-style type (container endpoint and mount declared elsewhere):

```csharp
[ServiceId("postgres")]
[ContainerDefaults("postgres", Tag = "16", Ports = new[] { 5432 }, Env = new[] { "POSTGRES_USER=postgres", "POSTGRES_DB=sora" })]
[EndpointDefaults(EndpointMode.Container, "postgres", "postgres", 5432, UriPattern = "postgres://{host}:{port}")]
public sealed class PostgresAdapterFactory {}
```

HTTP service with health:

```csharp
[ServiceId("weaviate")]
[ContainerDefaults("weaviate", Tag = "1.24", Ports = new[] { 8080 })]
[EndpointDefaults(EndpointMode.Container, "http", "weaviate", 8080)]
[HealthEndpointDefaults("/v1/.well-known/ready", IntervalSeconds = 10, TimeoutSeconds = 2, Retries = 12)]
public sealed class WeaviateAdapterFactory {}
```

Local vs container endpoints (both):

```csharp
[EndpointDefaults(EndpointMode.Container, "http", "api", 8080)]
[EndpointDefaults(EndpointMode.Local, "http", "localhost", 5080)]
```

## Verification

- Build the solution. The generator runs at compile time.
- Inspect the generated file (optional): `obj/Debug/net*/**/__SoraOrchestrationManifest.g.cs` in your project.
- From the project folder, run:

```pwsh
Sora inspect
Sora export compose --profile Local
```

You should see your service in the Context Card and in `.sora/compose.yml` with ports/env/volumes. If you declared HTTP health, the compose service will include a curl-based healthcheck and upstream services will depend on `service_healthy`; otherwise `service_started`.

## Common pitfalls

- Missing ServiceId or image → service won’t appear. Ensure both are present.
- Endpoint scheme not http/https but HealthEndpointDefaults set → health is ignored by design.
- Using only Local endpoint without Container endpoint → exporter still uses the container ports, but health and hints prefer container-mode metadata.
- Volumes duplicated: if you also declare host binds in your plan, exporters won’t add `HostMountAttribute`-based mounts (no duplication), but double-declaration in `ContainerDefaults.Volumes` is on you.

## References

- Reference — Orchestration: ../reference/orchestration.md
- Engineering — Orchestration adapter authoring: ./orchestration-adapter-authoring.md
- Architecture Principles: ../architecture/principles.md
