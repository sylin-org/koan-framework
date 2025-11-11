# WEB-0064 Koan.Context monitoring remediation

**Status**: Proposed 2025-11-10 \
**Drivers**: Monitoring accuracy, Koan.Context UX, Operations readiness \
**Deciders**: Koan Framework maintainers \
**Inputs**: `EnhancedMetrics` service, `MonitoringDashboard.tsx`, QA regression report \
**Outputs**: Corrected monitoring metrics contract, navigation entry, implementation backlog \
**Related**: WEB-0061, DATA-0061, ARCH-0013

**Contract**  
Inputs: Existing Koan.Context monitoring stack, QA analysis of metric drift.  
Outputs: Accepted diagnosis and remediation plan for monitoring accuracy and discoverability.  
Error Modes: Stale vector counts, misleading component health, hidden monitoring surface.  
Acceptance Criteria: ADR documents root cause with actionable fixes, TOC updated, downstream teams can prioritize implementation.

## Context

Recent QA sessions highlighted three gaps in the Koan.Context monitoring surface:

- Operators reported oscillating vector totals during re-index operations even when the vector outbox still contained unsynced chunks. Dashboard values jumped between ~250 and ~60 despite the vector store never dropping below ~180.
- Component health flags always rendered as "healthy" for the file monitor and outbox worker, masking stalls that were visible in logs.
- The `/monitoring` route is unreachable from the sidebar, so the dashboard is effectively hidden outside of deep links.

We reviewed the service and UI code to understand why numbers drift and why health cards mislead.

## Findings

1. **Vector totals derive from persisted project aggregates**. `EnhancedMetrics.GetVectorDbMetricsAsync` (src/Services/code-intelligence/Koan.Service.KoanContext/Services/EnhancedMetrics.cs, lines 438-476) sums `Project.DocumentCount` and assumes each count reflects the number of vectors currently stored. During re-index, the indexer resets `Project.DocumentCount` to the chunk count returned by the relational store (`Chunk.All`). Vector sync is asynchronous, so the UI shows future-state counts while the vector outbox still holds pending work. Growth calculations (`vectorsAddedToday`) also add full project totals (`project.DocumentCount`) instead of deltas, overstating daily intake.
2. **Storage metrics reuse the same stale aggregate**. `GetStorageMetricsAsync` (lines 486-537) relies on `Project.IndexedBytes` and `Project.DocumentCount`. When a project fails mid-run, these numbers freeze even though chunks and files continue to drift in the relational store, so the dashboard under-reports fresh chunk totals and never surfaces "stale" projects correctly.
3. **Component health checks are placeholders**. `CheckFileMonitorHealth` (line 258) returns "healthy" unconditionally. `CheckOutboxWorkerHealthAsync` (lines 282-327) only checks for operations with `LastAttemptAt == null`, meaning stalled workers that keep retrying never flip to warning. The monitoring UI therefore renders success badges while the outbox queue continues to grow.
4. **Project status API omits active job data**. `ProjectsController.GetProjectStatus` leaves `Job? activeJob = null;` without populating it (src/…/Controllers/ProjectsController.cs, lines 129-144). The project detail page expects `status.activeJob` for progress bars; with the placeholder, the UI cannot reflect live indexing progress or ETA.
5. **Monitoring navigation is absent**. `Layout.tsx` (src/…/ui/src/components/Layout.tsx, lines 28-43) defines the sidebar but does not include the `/monitoring` route, so operators cannot discover the page. The quick link inside the dashboard is moot when you cannot reach the page initially.

## Decision

We will realign Koan.Context monitoring around verifiable sources and expose the route visibly.

1. Introduce a `VectorIndexTelemetry` service that reconciles:
   - Actual vector counts from the configured adapter (`Vector<Chunk>.Stats` or provider-specific stats API) _when available_.
   - Relational chunk totals (`Chunk.CountAsync`) for a fallback path.
   - Pending vector operations from `SyncOperation.Query(...)` to surface `VectorsPending` and `VectorsBehindSeconds` so operators see backlog immediately.
     Enhanced metrics will call this service instead of summing project aggregates.
2. Add a `StorageFootprintSnapshot` helper that reads chunk/file totals via `Chunk.Query(...)` and `IndexedFile.Query(...)`, and calculates per-project freshness using the last successful job completion time. `Project.IndexedBytes` becomes secondary metadata, not the dashboard source of truth.
3. Replace placeholder health checks with instrumented probes:
   - `FileMonitoringService` must emit its last heartbeat; the health check will flag warning beyond 2× cadence.
   - The outbox worker health check will include retrying operations by inspecting `SyncOperation.LastAttemptAt` and `RetryCount`; we promote "warning" when retries exceed thresholds and "critical" when the queue ages past 5× the polling window.
   - We will reuse metrics from `MetricsCollector` to ensure UI cards match exported telemetry counters.
4. Update `ProjectsController.GetProjectStatus` to retrieve the most recent non-completed job for the project via `Job.Query(...)` (scoped to Planning/Indexing statuses) and project it into the response so the project detail page displays progress, elapsed time, and ETA during active indexing.
5. Expose the monitoring route in the primary navigation (`Layout.tsx`) with an icon that makes the page discoverable, and retain the route-level quick link for cross-navigation.
6. Extend `MetricsCollector` to capture `vector.total`, `vector.pending`, and `vector.synced` gauges so future dashboards and external exporters stay consistent.

## Consequences

- Metrics recalculation will add two extra queries (`Chunk.CountAsync` and `IndexedFile.CountAsync`) per refresh. We will cache snapshots for 5–10 seconds to keep the P0 dashboard fast.
- Vector adapters without `Stats` support must implement `IVectorCapabilities.Stats`; until then, the telemetry service falls back to chunk totals and marks the provider as "estimated" so the UI labels counts clearly.
- Health probes depend on new heartbeat data from `FileMonitoringService` and outbox worker progress updates; those producers acquire additional instrumentation obligations.
- The navigation change surfaces monitoring to every operator, so the dashboard must stay accurate; we will coordinate rollout with the telemetry fixes to avoid exposing outdated panels.
- API clients that already consumed `GET /api/projects/{id}/status` will receive additional `activeJob` payloads. The shape follows existing UI expectations (`progress`, `chunksCreated`, etc.), so only clients that assumed `activeJob` was always null need to adjust.

## Implementation notes

1. Land the `VectorIndexTelemetry` and `StorageFootprintSnapshot` services under `Koan.Context.Services` and inject them into `EnhancedMetrics`. Replace direct `Project.All` usage with service outputs and update `MetricsCollector.UpdateVectorStats` callers accordingly.
2. Instrument `FileMonitoringService` and `VectorSyncWorker` to record heartbeat timestamps and completed counts, then consume those signals in the health checks.
3. Populate `activeJob` in `ProjectsController.GetProjectStatus` by querying `Job.Query(j => j.ProjectId == id && (j.Status == JobStatus.Planning || j.Status == JobStatus.Indexing))` and ordering by `StartedAt` descending.
4. Update `Layout.tsx` to add a `Monitoring` entry (e.g., `Activity` icon) pointing to `/monitoring`; ensure the active route logic covers it.
5. Expand UI metric cards to display `vectorsPending` and to shade counts as "estimated" when they come from the fallback path.
6. After coding the above, rerun docs build via `pwsh ./scripts/build-docs.ps1` to validate links, and add regression tests that simulate a stalled outbox queue to verify health escalation.

## Rollout

- Implement telemetry services and health probes alongside unit tests that exercise empty project sets, large queues, and adapters without statistics.
- Update the SPA with the new metrics contract and navigation entry.
- Validate in staging with a re-index scenario where vector sync lags to ensure the dashboard distinguishes synced vs pending vectors.
- Publish follow-up documentation under `docs/api/web-http-api.md` once the endpoints stabilize.
