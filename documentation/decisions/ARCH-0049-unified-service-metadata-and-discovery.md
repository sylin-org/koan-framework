---
id: ARCH-0049
slug: unified-service-metadata-and-discovery
domain: Architecture
status: accepted
date: 2025-08-27
title: Unified service metadata and declarative discovery
---

## Context

The CLI contained multiple separation-of-concerns violations by inferring service roles (database, vector, AI, auth) from docker-compose files, image names, and project references. Inference is brittle and contradicts our attribute- and manifest-driven discovery model.

## Decision

Adopt a unified attribute and manifest schema for all service adapters and make CLI/Planner consume only declared metadata.

- Single attribute [KoanService] on adapter types implementing IServiceAdapter.
- Manifest fields include kind, shortCode, qualifiedCode, name, description, deploymentKind, containerImage, defaultTag, defaultPorts, healthEndpoint, capabilities, provides, consumes.
- Generator emits manifest JSON (schemaVersion=1) with the above fields, decoupled from runtime enums.
- Planner and CLI derive dependencies and grouping exclusively from declared kinds and relations; remove all heuristics from Program.cs.

## Scope

- Orchestration.Abstractions: new ServiceKind enum, IServiceAdapter/IKoanService interfaces, KoanServiceAttribute.
- Orchestration.Generators: extend manifest to emit unified fields.
- Orchestration.Cli: replace all heuristic detection with plan-based logic; render kind/type and image:tag where available.
- Adapters (DB/Vector/AI/Auth): annotate with [KoanService].

## Consequences

- Eliminates compose/csproj/image-name scraping in the CLI.
- Stable UX: container names/hostnames derive from shortCode; Inspect JSON includes kind and codes.
- Analyzer can enforce correct usage and naming; fewer runtime surprises.

## Implementation notes

- Keep Kind a closed enum; Subtype an open string taxonomy.
- Support simple shortCode (e.g., "mongo") and fully-qualified code (e.g., "Koan.db.relational.postgres").
- Include container image and tag when deploymentKind==Container; allow profile overrides.
- Known capability keys per kind; allow vendor.* extensions.

## Follow-ups

- Add the attribute, interfaces, and analyzer scaffolding in Abstractions.
- Update the generator to emit new fields; migrate first-party adapters.
- Refactor Planner to use provides/consumes when available.
- Update docs: engineering front door and reference pages; add examples.

## References

- ARCH-0045 Foundational SoC namespaces and Abstractions
- ARCH-0044 Standardized module config and discovery
- WEB-0044 Web auth discovery and health
- OPS-0049 Recs sample mongo/weaviate/ollama
