# JOBS-0001: Jobs Pillar — Entity-First Task Management

> **Status: Superseded by JOBS-0005.** The Job Orchestrator rebuild is a greenfield replacement that discards the original `Koan.Jobs.Core` module described here.

**Status**: Superseded by JOBS-0005
**Date**: 2026-03-25
**Deciders**: Enterprise Architect
**Scope**: New pillar — Koan.Jobs.Core, Koan.Scheduling integration
**Related**: ARCH-0074 (gap analysis, Phase 4), KOAN-JOBS-PROPOSAL (design document)

---

## Context

Long-running task management is a common need across Koan applications. The design was documented
in `docs/archive/design/KOAN-JOBS-PROPOSAL.md` (approved 2025-10-03) with a detailed 8-week implementation
plan in `docs/archive/design/jobs/IMPLEMENTATION-PLAN.md`. This ADR formalizes the proposal as an accepted
architectural decision.

## Decision

Adopt the Jobs pillar as described in KOAN-JOBS-PROPOSAL v3.0. Core tenets:

1. **Entity-first**: Jobs are `Entity<T>` with GUID v7 auto-generation
2. **Reference = Intent**: Adding `Koan.Jobs` package auto-enables infrastructure
3. **Semantic ergonomics**: `await MyJob.Start(context).Run()`, `await job.Wait()`
4. **Provider transparency**: Same code works across SQL/NoSQL/Vector/JSON stores
5. **Observable by default**: Progress tracking, ETA estimation, execution history

### Architecture

- `Koan.Jobs.Core`: Domain layer (Job, JobExecution, RetryPolicy, JobIndexCache)
- `Koan.Scheduling`: Execution layer (cron, recurring, one-shot)
- Data connectors: InMemory (default) + Postgres (production) via existing adapters
- Web: JobsController inheriting EntityController<T>

### Key Breaking Decisions (from design review)

- Single `Completed` status (no `Succeeded` — use result metadata)
- Infrastructure fields moved to `JobIndexCache` (not on Job entity)
- Retry defaults: 3 attempts, 5s delay, exponential backoff
- Archival: 30-day retention, enabled by default
- Cancellation: ephemeral (memory-based, not persisted)

### Implementation Plan

6 milestones over 8 weeks — see `docs/archive/design/jobs/IMPLEMENTATION-PLAN.md`.

## Consequences

### Positive
- First-class job management aligned with entity-first patterns
- Reusable across all Koan applications
- Observable via OpenTelemetry integration

### Negative
- Requires `[Timestamp]` attribute support in Koan.Data.Core (prerequisite)
- 8-week implementation commitment

## References

- [KOAN-JOBS-PROPOSAL](../archive/design/KOAN-JOBS-PROPOSAL.md) — Full design document (v3.0)
- [Implementation Plan](../archive/design/jobs/IMPLEMENTATION-PLAN.md) — 6-milestone roadmap
- [Architectural Decisions](../archive/design/jobs/ARCHITECTURAL-DECISIONS.md) — 12 approved ADRs
- ARCH-0074: Framework Gap Analysis (Phase 4 tracks execution)
