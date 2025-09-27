# S13.DocMind Next Steps (Delta Focus)

## Bootstrap & Health Readiness Gaps
1. Extend `DocMindRegistrar.Describe` to execute real storage, vector, and AI readiness probes (e.g., ensure buckets exist, ping vector adapters, dry-run model invocations) so the boot report reflects live status instead of static configuration listings. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L46-L79】

## Stage-Oriented Worker & Durable Retries
1. Expand the new `DocumentProcessingWorker` to capture per-stage metrics/tokens and emit richer context on start/end events, ensuring diagnostics can trace retries and resumptions. 【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L80-L470】
2. Project the `DocumentProcessingJob` ledger through diagnostics (paging, filtering, stage summaries) and surface it via APIs/UI so operators can monitor backlog health. 【F:samples/S13.DocMind/Models/DocumentProcessingJob.cs†L1-L120】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L260】
3. Finalize targeted stage replay by wiring controller/CLI flows that update jobs, append timeline events, and guard against concurrent runs. 【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L70】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L120-L220】

## Discovery & Analytics Alignment
1. Replace `Entity<T>.All()` fetches in insights/aggregation services with repository-driven queries that push filters, paging, and projections server-side, matching the discovery blueprint. 【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L29-L146】
2. Materialize aggregation feeds and processing timelines from repository APIs (e.g., batched `ProcessingEventRepository` queries) instead of recomputing per-request LINQ to support MCP paging and dashboards. 【F:samples/S13.DocMind/Infrastructure/Repositories/ProcessingEventRepository.cs†L12-L45】

## Vector & AI Integration Completion
1. Swap the manual cosine loop for adapter-backed vector search with availability gating and fallbacks mirroring S5.Recs, while logging vector diagnostics alongside stage events. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L124-L196】【F:samples/S5.Recs/Services/RecsService.cs†L96-L149】
2. Expand vector bootstrapping to cover semantic profile embeddings (not just chunk indices) and publish readiness statistics for diagnostics. 【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L11-L36】
3. Capture AI/vector latency, token, and model readiness metrics in processing events so downstream analytics can report quality and cost. 【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L200-L360】

## Operational Hardening & Quality Gates
1. Register storage/vector/AI health checks and expose queue backlog telemetry through MCP/UI endpoints, complementing the richer boot report. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L46-L79】
2. Wrap pipeline stages with OpenTelemetry spans (or equivalent) so duration/token metrics land in `DocumentProcessingEvent` and tracing sinks. 【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L200-L360】
3. Stand up DocMind-focused integration tests (upload→completion, targeted retries, vector-disabled fallbacks) using fake providers to guard the refactored worker and discovery services.

## Immediate Spikes & Research Tasks
1. Load-test the persisted worker ledger/resume path to validate concurrency, retry jitter, and migration needs before broadening rollout.
2. Draft repository queries/aggregations for dashboards and timeline endpoints, validating them against sample data to ensure the discovery refactor lands smoothly.
3. Evaluate adapter search capabilities (Weaviate or alternative) and outline graceful-degradation paths when vector services are offline, borrowing the S5.Recs query/fallback layering as a reference implementation. 【F:samples/S5.Recs/Services/RecsService.cs†L96-L149】
4. Scope the health-check/telemetry wiring and DocMind-focused test harness so they can ship alongside the worker refactor instead of trailing it.
