---
title: Orchestration adapter authoring — deprecated (see ARCH-0049 unified attributes)
description: This legacy guidance predates ARCH-0049. Use SoraService/SoraApp and the manifest-first model. Endpoint/mount hints remain as optional extras for exporters.
---

> Deprecated: This page documents the pre-ARCH-0049 authoring model. The CLI no longer relies on image-name heuristics or reflection-based defaults. Prefer the unified attributes: [SoraService] for services and [SoraApp] for the app anchor. See: engineering/orchestration-manifest-generator.md

# Orchestration adapter authoring — endpoints and persistence mounts (legacy)

Contract (at a glance)
- Inputs: adapter factory/type annotated with attributes.
- Outputs: endpoint hints for UX (scheme and optional UriPattern) and persistence mount paths consumed by exporters.
- Error modes: missing or ambiguous metadata results in heuristic fallbacks (image/port based) but should be avoided.
- Success: `sora status` renders deterministic endpoints and Compose export injects correct `./Data/{service}:{containerPath}` binds without duplication.

## Attributes

DefaultEndpointAttribute
- Purpose: advertise default scheme and optional `UriPattern` for a container’s exposed port(s), plus image prefixes for discovery.
- Signature: `DefaultEndpointAttribute(string scheme, string defaultHost, int containerPort, string protocol = "tcp", params string[] imagePrefixes)`; optional `UriPattern` property, e.g., `"postgres://{host}:{port}"`.
- Precedence: when `UriPattern` is set, it must be used to render endpoints in status and logs instead of `scheme://host:port`.

HostMountAttribute
- Purpose: declare one or more container paths that should be persisted via host bind mounts for local dev (e.g., databases).
- Signature: `HostMountAttribute(string containerPath)`; can be repeated to declare multiple paths.

Discovery
- Manifest-first: planners and CLI consume `__SoraOrchestrationManifest.Json` exclusively.
- Exporters may still use these hints (when present) to improve UX (endpoints) and add persistence mounts safely.

## Minimal examples

Annotating a Postgres adapter:

// C#
// inside your adapter factory/type
[DefaultEndpoint(
    scheme: "postgres",
    defaultHost: "localhost",
    containerPort: 5432,
    protocol: "tcp",
    imagePrefixes: new[]{ "postgres", "mycorp/postgres" },
    UriPattern = "postgres://{host}:{port}")]
[HostMount("/var/lib/postgresql/data")]
public sealed class PostgresAdapterFactory { /* ... */ }

Declaring multiple mount targets:

// C#
[DefaultEndpoint("http", "localhost", 8080, "tcp", new[]{ "weaviate" })]
[HostMount("/var/lib/weaviate")]
[HostMount("/var/lib/weaviate/backups")]
public sealed class WeaviateAdapterFactory { /* ... */ }

## Edge cases and notes

- If no matching `ImagePrefixes` are found for a service image, exporters may use heuristics (e.g., known images like postgres/redis). Prefer explicit prefixes to avoid drift.
- When a plan already declares a volume for a target path, exporters do not add a duplicate bind.
- `UriPattern` placeholders: `{host}` and `{port}` must appear exactly once; additional tokens are not substituted.
- Only stable, container-internal absolute paths should be used in `HostMountAttribute`.

## See also

- Engineering — Orchestration manifest generator (unified): ./orchestration-manifest-generator.md
- Decision — ARCH-0049 unified service metadata and discovery: ../decisions/ARCH-0049-unified-service-metadata-and-discovery.md
- Decision — ARCH-0048 Endpoint resolution and persistence mounts: ../decisions/ARCH-0048-endpoint-resolution-and-persistence-mounts.md
- Reference — Orchestration: ../reference/orchestration.md
