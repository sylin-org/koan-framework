---
title: Orchestration adapter authoring — endpoints and persistence mounts
description: How to declare adapter endpoint hints (schemes and URI patterns) and persistence mount paths used by the CLI and Compose exporter.
---

# Orchestration adapter authoring — endpoints and persistence mounts

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
- The CLI and Compose exporter reflect across loaded assemblies and match by `ImagePrefixes` (prefix match on the service image name).
- Multiple `HostMountAttribute`s per type are supported; each target is injected once if not already declared in the plan.

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

- Reference — Orchestration: ../reference/orchestration.md
- Decision — ARCH-0048 Endpoint resolution and persistence mounts: ../decisions/ARCH-0048-endpoint-resolution-and-persistence-mounts.md
- Engineering — Front door: ./index.md
