# S13.DocMind Refactoring Plan (Proposal Delta)

## Remaining Gaps
- **Ledger projections are still in-memory scans without stage telemetry.** The proposal expects the worker and diagnostics to poll durable stage/status slices and emit replay-ready breadcrumbs, yet `DocumentProcessingJobRepository` and the diagnostics queue enumerate the entire ledger and fetch every `SourceDocument` on each request, leaving no persisted timing/token metrics for later paging or resume flows.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L5-L39】【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L176】【F:samples/S13.DocMind/Infrastructure/Repositories/DocumentProcessingJobRepository.cs†L25-L41】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L38-L136】
- **Discovery services bypass the projection strategy.** Dashboards still call `SourceDocument.All()`/`DocumentInsight.All()` instead of repository-backed aggregates (`InsightCollection`, timeline feeds) the blueprint describes, so nothing supports server-side filtering, freshness metadata, or paging for UI/MCP clients.【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L182】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L52】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】
- **Vector-powered suggestions remain local cosine checks.** The implementation loads every profile embedding in-process and never calls `Vector<T>.SearchAsync` or validates semantic profile readiness, diverging from the Weaviate-backed adapter and bootstrap requirements (mirroring the S5.Recs fallback pattern).【F:docs/chunks/S13-DocMind/03_ai_processing.md†L18-L30】【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L12】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L128-L196】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:samples/S5.Recs/Services/RecsService.cs†L96-L155】
- **Operational safeguards and tests lag the infrastructure plan.** The boot report still lists configuration only, there are no storage/vector/model health probes, OpenTelemetry spans, or DocMind-specific test projects/CI gates described in the testing and operations blueprint.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L73】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L45-L79】

## Refactoring Streams

### 1. Stage Ledger & Diagnostics
1. **Introduce provider-side queries.** Add repository filters (status, stage, due window, correlation) and expose them through diagnostics endpoints to eliminate full-table scans and enable queue paging the worker can reuse.【F:samples/S13.DocMind/Infrastructure/Repositories/DocumentProcessingJobRepository.cs†L25-L41】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L38-L115】
2. **Persist per-stage breadcrumbs.** Extend `DocumentProcessingJob`/event entries with started/completed timestamps, token counts, and retry history so the API can answer the replay/observability scenarios from the proposal without recomputation.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L5-L39】【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L176】
3. **Wire replay tooling to the ledger.** Update `ProcessingController`/CLI flows to request stage-specific resumes against the new queries, enforcing concurrency guards and returning correlation metadata for MCP tooling.【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L40】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L140-L200】

### 2. Discovery Projections
1. **Create aggregation repositories.** Materialize `InsightCollection`, queue, and similarity projections (with refreshed-at metadata) so discovery APIs can query a single projection per dashboard rather than enumerating documents/insights.【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L182】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】
2. **Expose paged analytics endpoints.** Extend diagnostics controllers to accept pagination, filters, and projection timestamps, aligning with the UI/API expectations noted in the blueprint.【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L92】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L38-L136】
3. **Retire ad-hoc insight aggregation.** Replace in-memory overview/collection math with the new projections to unlock MCP and Angular consumers without duplicate logic.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】

### 3. Vector & Template Alignment
1. **Adopt adapter-backed search.** Replace the manual cosine scoring with `Vector<SemanticTypeEmbedding>.SearchAsync`, capturing adapter latency/fallback data in suggestion diagnostics just like S5.Recs.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L128-L196】【F:samples/S5.Recs/Services/RecsService.cs†L96-L155】
2. **Bootstrap semantic profiles.** Extend the vector bootstrapper (or add a companion job) to validate/create semantic profile embeddings and block suggestions when adapters are unavailable, matching the infrastructure readiness checklist.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L12】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】
3. **Publish vector health to diagnostics.** Surface adapter status, last-sync timestamps, and fallback state through the boot report and queue/timeline projections so operators understand degraded modes.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L45-L79】

### 4. Operations & Quality Gates
1. **Implement health probes.** Add storage/vector/model checks in `DocMindRegistrar` and expose matching `/health` endpoints to satisfy the infrastructure playbook.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L45-L79】
2. **Instrument telemetry.** Wrap AI stage calls with OpenTelemetry spans and persist token/duration metrics on processing events to support the observability expectations.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L55-L59】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L200-L360】
3. **Deliver DocMind test suites and CI gates.** Stand up the unit/integration projects, fake AI providers, and pipeline smoke tests described in the testing plan, wiring them into automation to guard the refactor.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L67】

## Delivery Order
1. **Iteration 1 – Ledger projections.** Ship filtered job queries, stage telemetry persistence, and diagnostics updates so replay tooling has durable data.
2. **Iteration 2 – Discovery aggregates.** Introduce insight/queue projections and update APIs to use them with paging/filter metadata.
3. **Iteration 3 – Vector readiness.** Implement adapter-backed search, semantic profile bootstrapping, and vector diagnostics.
4. **Iteration 4 – Operations & tests.** Layer in health checks, telemetry, and DocMind-specific test automation to close the remaining infrastructure delta.

Each iteration keeps the sample runnable while erasing the outstanding proposal deltas.
