# S13.DocMind Refactoring Plan (Comprehensive Delta Review)

## 1. Current Delta Snapshot
- **Bootstrap uses minimal Koan pattern** – `Program.cs` calls only `AddKoan()` which automatically discovers `DocMindRegistrar : IKoanAutoRegistrar` and registers all services, hosted workers, and options validation without manual intervention. 【F:samples/S13.DocMind/Program.cs†L1-L9】
- **Stage-aware worker landed but needs richer telemetry** – `DocumentProcessingWorker` now processes each stage against a durable `DocumentProcessingJob` ledger, replacing the volatile channel queue, yet per-stage metrics and diagnostic projections remain thin. 【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L1-L470】【F:samples/S13.DocMind/Models/DocumentProcessingJob.cs†L1-L120】
- **Diagnostics must project the job ledger** – Queue/timeline APIs still shape data from in-memory assumptions; `ProcessingDiagnosticsService` should page and filter the persisted jobs so MCP tooling surfaces backlog health and targeted resumptions. 【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L260】【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L70】
- **Repositories and discovery services still perform collection scans** – Timeline, insights, and aggregation services hydrate entire tables through `Entity<T>.All()` or ad-hoc filters, contradicting the proposal’s repository-driven, server-side projection strategy and limiting scalability. 【F:samples/S13.DocMind/Infrastructure/Repositories/ProcessingEventRepository.cs†L12-L45】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L28-L149】
- **Vector and semantic profile alignment remains partial** – Chunk embeddings persist to the adapter, but template suggestions still execute cosine similarity in-process after loading every embedding/profile, and no bootstrapper ensures semantic profile vectors exist. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L94-L209】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L34】
- **Operational hardening is unfinished** – Boot reports list configuration but omit health checks, AI/vector readiness probes, or telemetry hooks, and there is no automated coverage guarding the rebuilt pipeline. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L40-L86】

## 2. Layered Remediation Strategy

### Layer A – Bootstrap & Configuration Optimization
1. **Leverage pure auto-registration** – Current `AddKoan()` already discovers `DocMindRegistrar` automatically; no additional assembly loading needed as the framework handles application assembly discovery.
2. **Move options to registrar** – Configuration and validation now handled within `DocMindRegistrar.Initialize()` following the "Reference = Intent" principle where modules manage their own dependencies.
3. **Enhance boot readiness checks** – Extend `DocMindRegistrar.Describe` to run storage/vector/AI health probes and emit actionable warnings instead of static settings dumps.

### Layer B – Processing Topology & Stage Semantics
1. **Enrich the worker timeline** – Emit per-stage metrics/tokens, record stage start/stop events, and persist retry cadence so diagnostics can visualize pipeline health directly from the job ledger.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L80-L470】
2. **Surface backlog projections** – Extend diagnostics/controllers to query and page `DocumentProcessingJob` records, exposing queue depth, retry state, and stage summaries to operators.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L260】【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L70】
3. **Guard targeted resume flows** – Ensure requeue endpoints persist requested stages, append timeline breadcrumbs, and prevent concurrent executions for the same document.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L120-L220】【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L150-L210】

### Layer C – Data Access & Aggregations
1. **Repository-first projections** – Build dedicated repository helpers (documents, chunks, insights, processing events) that generate server-side filters for timelines, queue views, and dashboards; remove `Entity<T>.All()` calls from services/controllers.
2. **Insight collections & feeds** – Materialize the `InsightCollection`/`SimilarityProjection` aggregates from the blueprint with scheduled refreshes so the discovery layer queries precomputed rollups.
3. **Timeline API alignment** – Move `/processing/timeline` to the new repository APIs, adding pagination/windowing so MCP tooling can stream events without loading the entire dataset.

### Layer D – AI & Vector Integration
1. **Adapter-backed similarity** – Replace manual cosine comparisons with `Vector<SemanticTypeEmbedding>.SearchAsync`, capturing adapter latency and emitting `GenerateEmbeddings` events when vectors are written.
2. **Semantic profile bootstrapping** – Expand `DocumentVectorBootstrapper` (or add a new initializer) to ensure semantic profile embeddings exist and regenerate stale vectors during startup.
3. **Model readiness telemetry** – Validate configured AI models and emit structured boot warnings/metrics when providers are absent; propagate model/latency data into processing events for diagnostics.

### Layer E – Diagnostics, Ops & Quality
1. **Queue & pipeline health checks** – Register Koan health checks for storage paths, vector adapters, AI providers, and queue backlog thresholds, wiring them into MCP/UI status surfaces.
2. **Structured telemetry & tracing** – Wrap pipeline stages with OpenTelemetry spans, enrich `DocumentProcessingEvent` records with duration/token metrics, and surface correlation IDs through the API.
3. **Automated coverage** – Add integration tests covering upload→completion, targeted retries, and vector-disabled fallbacks using Koan in-memory adapters to guard the rebuilt flow.

## 3. Execution Roadmap (Break-and-Rebuild Friendly)
1. **Iteration 0 – Bootstrap Reactivation (Layer A)**: Activate module loading, tighten options validation, and extend boot diagnostics/health probes.
2. **Iteration 1 – Stage-Oriented Worker (Layer B)**: Introduce the polling worker, persist work state, and emit full stage telemetry with targeted retries.
3. **Iteration 2 – Repository-Driven Insights (Layer C)**: Deliver repository helpers, insight collections, and timeline alignment while retiring `All()` usages.
4. **Iteration 3 – AI/Vector Completion (Layer D)**: Move suggestions to adapter-backed search, bootstrap semantic profiles, and add model readiness telemetry.
5. **Iteration 4 – Operational Hardening (Layer E)**: Ship health checks, tracing, and integration tests before refreshing docs/tooling.

Each milestone should leave the sample runnable (feature flags permitted) while progressively restoring parity with the S13.DocMind proposal.
