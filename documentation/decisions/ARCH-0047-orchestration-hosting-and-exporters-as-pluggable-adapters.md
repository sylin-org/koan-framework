---
id: ARCH-0047
slug: orchestration-hosting-and-exporters-as-pluggable-adapters
domain: Architecture
status: approved
date: 2025-08-26
title: Orchestration — Hosting providers and Exporters as pluggable adapters
---

## Context

Koan aims to offer first-class orchestration with a library-first, intention-driven model: reference = intent, recipes amplify behavior, and configuration remains authoritative. We need a simple, DX-first way to bring up local dependencies and export portable artifacts without binding to a specific environment.

Key drivers
- DX: one-command start, meaningful defaults, explainable decisions, safe by default.
- Environment-agnostic architecture with a Windows-first initial deliverable.
- No bespoke YAML DSL; code/config are the source of truth; artifacts are generated.

## Decision

We will introduce a small orchestration SPI and treat hosting runtimes and artifact export as pluggable adapters:

- Hosting providers (runtimes): Docker and Podman as first-class adapters selected by availability and preference.
- Artifact exporters: Compose (v1), Helm (vNext), Azure Container Apps/Bicep (vNext) as separate pluggable exporters.
- Core orchestrator builds a deterministic Plan from adapter/recipe-contributed service descriptors (image, env, ports, volumes, health, depends_on) gated by Koan:* options and profiles.
- A lightweight CLI ("Koan") provides dev init/up/down/logs/status/doctor and export commands with verbosity flags.

Defaults (approved)
- Windows-first provider order: Docker → Podman; override via --engine.
- Compose is the initial export; Helm/ACA follow.
- Profiles via Koan_ENV (local|ci|staging|prod), default = local.
- Heavy AI (e.g., Ollama) is opt-in (profile/flag/config); SQLite is never containerized.
- Named volumes by default (avoid bind mounts) for cross-engine behavior.

## Scope

In scope
- SPI contracts: IHostingProvider, IArtifactExporter, IDevServiceDescriptor, Profile model.
- CLI UX and flags (-v/-vv/--trace/--quiet, --explain, --dry-run, --json).
- Docker provider, Podman provider, Compose exporter; discovery/selection logic; doctor/status UX.

Out of scope (this ADR)
- Helm and ACA exporter details (follow-up ADRs if needed).
- Secret management; exporters only reference external secrets by name.
- Owning cluster lifecycle; Koan only generates artifacts and runs local stacks.

## Consequences

Positive
- Modular, testable, and vendor-neutral; adapters can evolve independently.
- Preserves code-first posture and leverages existing Recipes and health/readiness.
- Clear, explainable behavior with strong defaults and minimal configuration.

Tradeoffs / Risks
- Adapter coverage work (initial set: Postgres, Mongo, Redis, RabbitMQ, Weaviate, Ollama).
- CLI/engine drift (Docker/Podman); mitigated by doctor checks and feature flags.
- Artifact exporter maintenance (Helm/ACA) requires ongoing attention.

## Implementation notes

Contracts
- IDevServiceDescriptor: ShouldApply(cfg) → bool|reason; Describe(profile) → ServiceSpec { image, env, ports, volumes, health, depends_on }.
- IHostingProvider: IsAvailableAsync(); Up/Down/Logs/Status; EngineInfo; Supports(features).
- IArtifactExporter: Supports(format); Generate(plan, profile, outPath); Capabilities.

Planner and selection
- Deterministic activation: explicit config → ON; recipe with config → ON; package presence alone is a hint (requires minimal config to activate).
- Provider precedence configurable (Koan:Orchestration:PreferredProviders); override with --engine.

DX and safety
- Readiness gates are honored; ports auto-avoid conflicts with clear messages; sensitive values are redacted in outputs.
- Local OTel exporters are opt-in via Observability recipe or flag.

## Follow-ups
- Implement Compose exporter, Docker provider, Podman provider (Windows-first), and CLI (Phase 1).
- Add Helm exporter and ACA/Bicep exporter (Phase 3).
- Add Policy Packs (warn-only by default) and CLI scaffolds (Koan new …) later.

## References
- ARCH-0046 — Recipes: intention-driven bootstrap and layered config
- ARCH-0040 — Config and constants naming
- Architecture Principles — docs/architecture/principles.md
