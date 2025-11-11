# ADR-0056: Koan.Context Maintenance and Reconciliation Surface

**Status:** Accepted  
**Date:** 2025-11-10  
**Context:** Koan.Context service lifecycle hardening and maintenance UX  
**Decision Makers:** Koan architecture group, Context pillar leads  
**Affected Components:** Koan.Service.KoanContext (indexer, maintenance services, UI), Koan.Data.Vector, Koan.Core hosting bootstrap  
**Supersedes:** Implicit flush-on-index behaviour introduced prior to ADR-0050

---

| **Contract** | **Details** |
| --- | --- |
| **Inputs** | Project registry (`Project`), indexed metadata (`IndexedFile`, `Chunk`), vector sync outbox (`SyncOperation`), live file system snapshot, maintenance command payloads (UI/API). |
| **Outputs** | Deterministic maintenance jobs with audit logs, reconciled chunk metadata, replayed vector sync operations, targeted wipe results (metadata-only or vector-only), updated project/job status. |
| **Error Modes** | Partition lock unavailable → surface 409 and refuse maintenance; file scan failure → partial results flagged and retriable; vector adapter offline → maintenance aborts prior to destructive steps; orphaned sync operations → moved to dead-letter with remediation guidance. |
| **Success Criteria** | No automatic destructive wipes during restart, reconciliation repairs drift without full re-index, selective wipes require explicit confirmation, operations observable via structured events, UI exposes “House Chores” workflow with safe defaults. |

---

## Context and Problem Statement

The Koan.Context indexer currently performs an unconditional "wipe and rebuild" when a project is reindexed: it flushes the Weaviate class via `Vector<Chunk>.Flush()` and deletes every chunk record before any new vectors are generated. When the subsequent indexing pipeline fails (for example, due to a missing dependency such as `TagResolver`), the system is left with no vectors or chunk metadata until a manual reindex completes. Users perceive this as data loss on restart.

Operators need transparent, surgical maintenance actions rather than a monolithic destructive routine. Typical scenarios include fixing drift after manual file deletions, recovering from vector store outages, and validating that metadata still matches the physical project. The current approach couples startup health, indexing, and maintenance in a way that amplifies failure impact.

## Decision Drivers

1. **Least-destructive defaults** – Restarts and routine maintenance must not erase data without explicit intent.
2. **Single source of truth** – Chunk metadata in SQLite drives reconciliation; vector state is rebuildable.
3. **Observable operations** – Maintenance actions must emit auditable events and metrics.
4. **Operator ergonomics** – Provide an opinionated "House Chores" workflow that solves the common drift case.
5. **Composable services** – Maintenance logic should be orchestrated, not duplicated across controllers, background tasks, and UI flows.

## Decision

We will replace the implicit wipe-on-reindex behaviour with an explicit maintenance surface that exposes three composable operations behind a dedicated service:

1. **ReconcileMetadata** (House Chores): scan the project file system, diff against `IndexedFile`/`Chunk`, delete orphaned records, enqueue new/changed files, and leave healthy data untouched.
2. **DropVectorArtifacts**: delete vector store entries for the partition via the adapter, then trigger vector rehydration using existing `SyncOperation` workflows.
3. **DropChunkMetadata**: remove chunk metadata only (keeping indexed files manifest) and schedule a targeted rebuild. This replaces the current `Flush + Delete` block.

### Service Architecture

- Introduce `IProjectMaintenanceService` with orchestration logic for the three operations and shared guardrails (partition locks, suspension of file watchers, audit logging).
- Extract the existing plan/diff logic from `Indexer` into a reusable `IndexingPlanner` component that can run in read-only mode for reconciliation.
- Expose the service via a controller (`MaintenanceController`) and the SPA UI, defaulting to `ReconcileMetadata` when an operator presses **House Chores**. Destructive wipes require a secondary confirmation and feature flag enablement.
- Update startup sequencing: `JobMaintenanceTask` will invoke only `ReconcileMetadata` (with a bounded timeout) instead of scheduling a full reindex. If the reconciliation detects missing vectors, it flags the project for follow-up rather than erasing additional data.

### Cleanup and Deprecations

- Remove the `Vector<Chunk>.Flush` call from `Indexer` and `IncrementalIndexer`. The only code paths allowed to perform wipes are the new maintenance operations.
- Eliminate legacy helpers that assumed a wipe-first rebuild, including bulk chunk deletion loops inside `Indexer.IndexProjectAsync`.
- Deprecate the hybrid UI affordances (alpha slider, instant flush buttons) and replace them with the new maintenance panel built around the House Chores workflow.

### Observability and Safety

- All maintenance actions write audit records (project id, operator, requested operation, duration, affected counts) and emit structured logs/metrics.
- Partition locking ensures only one maintenance job runs per project; attempts to start a second job return `409 Conflict` with guidance.
- The service suspends file monitoring before mutations and resumes it afterwards to avoid double-processing changes.
- In the event of partial failure, the service records progress markers so the operator can re-run only the failing segment.

## Consequences

### Positive

- Restarts no longer nuke vector data by default; reconciliation is idempotent and safe to repeat.
- Operators gain a consistent, auditable interface for maintenance without resorting to database scripts or full reindexes.
- The indexing pipeline focuses on positive work (processing new/changed files) instead of redundant deletions, improving completion time.
- Vector store failures are isolated: an operator can drop and replay vectors without risking metadata.

### Negative

- Initial implementation effort is higher: requires new service, controller, UI updates, and telemetry plumbing.
- Reconciliation scans may take longer than the previous wipe in extremely large projects; needs careful batching and progress reporting.
- Infrastructure for audit storage and operator identity must be present to deliver meaningful logs.

### Neutral / Open Questions

- We may add policy controls (e.g., role-based access to destructive wipes) once the base surface ships.
- Future vector adapters must implement selective partition wiping to comply with `DropVectorArtifacts` semantics.

## Adoption Plan

1. Implement `IProjectMaintenanceService` and migrate `JobMaintenanceTask` to depend on it.
2. Refactor `Indexer` to delete only the files explicitly scheduled by the planner; remove the blanket flush.
3. Add REST endpoints and SPA components for House Chores and gated wipes, replacing legacy controls.
4. Update documentation, including the search troubleshooting guide, to reference the new maintenance flows.
5. Roll out feature flags for destructive operations; enable House Chores by default once validated in staging.

## References

- ADR-0055 (Tag-Centric Semantic Search Rebuild)
- DATA-0085 (Vector module resilience)
- ARCH-0046 (Intent-driven bootstrap) – informs maintenance service discovery
