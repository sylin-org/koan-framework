# S13.DocMind Next Steps (Proposal Delta)

## Ledger & Diagnostics
1. **Design provider-backed job queries.** Prototype filtered `DocumentProcessingJob` lookups (status, stage, retry window, correlation) and reuse them in `/processing/queue` so the worker/diagnostics stop scanning the entire ledger.【F:samples/S13.DocMind/Infrastructure/Repositories/DocumentProcessingJobRepository.cs†L25-L41】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L38-L115】
2. **Capture stage metrics on the ledger.** Persist started/completed timestamps, token counts, and retry notes for each stage, then surface them through queue/timeline responses to unlock the replay/observability flows promised in the proposal.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L5-L39】【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L176】
3. **Update replay surfaces.** Extend `ProcessingController`/CLI to target specific stages using the new queries, returning correlation IDs and guardrails that MCP tooling can consume.【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L40】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L140-L200】

## Discovery & Analytics
1. **Implement projection repositories.** Build `InsightCollection`, queue, and timeline aggregates with refreshed-at metadata so dashboards avoid `SourceDocument.All()`/`DocumentInsight.All()` scans.【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L182】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】
2. **Expose paged diagnostics endpoints.** Add pagination, filters, and projection timestamps to `/processing/queue` and `/processing/timeline`, wiring results to the new repositories for UI/MCP parity.【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L92】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L38-L136】
3. **Retire ad-hoc insight math.** Swap the overview/collection calculations to consume the projections, ensuring freshness metadata and consistent filters.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】

## Vector & Template
1. **Use adapter-backed similarity.** Replace the manual cosine loop with `Vector<SemanticTypeEmbedding>.SearchAsync`, emitting latency/fallback diagnostics that match the S5.Recs approach.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L128-L196】【F:samples/S5.Recs/Services/RecsService.cs†L96-L155】
2. **Bootstrap semantic profile embeddings.** Extend the bootstrapper to ensure profile vectors exist, rebuild stale ones, and flag degraded states when the adapter is offline.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L12】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】
3. **Surface vector health.** Publish adapter readiness and last-sync timestamps through the boot report and diagnostics responses so operators understand fallbacks.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L45-L79】

## Operations & Quality
1. **Add health probes.** Implement storage/vector/model checks in `DocMindRegistrar` and expose `/health` endpoints per the infrastructure playbook.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L45-L79】
2. **Instrument pipeline telemetry.** Wrap AI stage calls with OpenTelemetry spans and persist token/duration metrics on processing events to satisfy the observability plan.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L55-L59】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L200-L360】
3. **Stand up DocMind test suites.** Create the unit/integration projects with fake AI providers and ledger-focused smoke tests, then wire them into CI as the testing plan outlines.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L67】

## Immediate Spikes
- **Query design spike:** Validate repository filters against sample data before refactoring the worker loop.【F:samples/S13.DocMind/Infrastructure/Repositories/DocumentProcessingJobRepository.cs†L25-L41】
- **Projection schema sketch:** Draft aggregation schemas for insight/queue/timeline projections to confirm they meet dashboard needs.【F:docs/chunks/S13-DocMind/02_entity_models.md†L169-L182】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】
- **Vector adapter capabilities check:** Exercise `Vector<T>.SearchAsync` and degraded-mode behavior to document fallbacks aligned with S5.Recs.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L128-L196】【F:samples/S5.Recs/Services/RecsService.cs†L96-L155】
- **Ops rollout planning:** Decide which health probes, telemetry exporters, and test suites land with the first iteration so operational hardening tracks the refactor.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L73】
