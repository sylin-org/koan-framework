---
id: ARCH-0048
slug: endpoint-resolution-and-persistence-mounts
domain: Architecture
status: accepted
date: 2025-08-26
title: Adapter-declared endpoint resolution (UriPattern precedence) and persistence mounts for orchestration
---

## Context

Earlier, the CLI inferred endpoint schemes from host/container ports (e.g., 80→http, 443→https). This violated separation of concerns and was brittle. Adapters best understand their protocols and connection shapes.

We also needed predictable, opt-in persistence for containerized adapters so data survives restarts. Developers expect a conventional local folder mapping (./Data/<service>). Compose exports should reflect that without bespoke per-adapter logic.

## Decision

1. Adapter-declared endpoints via attributes

- Introduce DefaultEndpointAttribute on adapter factories with: Scheme, DefaultHost, ContainerPort, Protocol, ImagePrefixes, and optional UriPattern.
- UriPattern, when present, takes precedence when rendering endpoints (status live/hints). It may include {host} and {port} placeholders.
- The CLI discovers these attributes at startup and registers an endpoint resolver. Endpoint formatting uses adapter-declared data first; otherwise falls back to conservative heuristics.

2. Persistence mounts via attributes

- Introduce HostMountAttribute(containerPath) for adapters to declare container data directories that should be bind-mounted in local dev.
- The Compose exporter discovers HostMount + DefaultEndpointAttribute(ImagePrefixes) pairs and automatically injects bind mounts: ./Data/{serviceId} -> <containerPath>, unless the plan already declares a binding to that path.
- A small image-name heuristic remains as a soft fallback when no attributes match.

## Scope

- Applies to orchestration CLI (endpoint hints, status live endpoints), Compose exporter, and data adapters that can be containerized.
- No behavior change for non-containerized modules (e.g., SQLite). Production/staging export semantics remain conservative.

## Consequences

- Port-based scheme inference is removed from public surface. Behavior is now adapter-driven and explicit; UriPattern ensures accurate URIs.
- Compose exports consistently persist adapter data under ./Data/{serviceId} by default in local scenarios, reducing surprise data loss.
- Slight startup reflection cost to discover attributes (bounded by loaded assemblies).

## Implementation notes

- Attributes live in Koan.Orchestration.Abstractions.
- CLI registers both a simple scheme resolver (legacy) and a richer endpoint resolver (scheme + pattern). EndpointFormatter prioritizes the richer resolver and applies UriPattern first.
- Compose exporter enriches ServiceSpec.Volumes with host bind mounts based on attribute discovery; named volumes already declared are preserved unmodified.
- Adapters annotated (initial set): Postgres, Mongo, Redis, SQL Server, Weaviate.

## Follow-ups

- Add tests for compose mount injection and pattern-based endpoint formatting.
- Consider extending attributes for multiple container paths and profile-scoped behaviors.
- Document adapter authoring guidance for these attributes under developer docs.

## References

- ARCH-0047 - Orchestration: hosting providers and exporters as adapters
- docs/reference/orchestration.md
- docs/engineering/orchestration-spi.md
