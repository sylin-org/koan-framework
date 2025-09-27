# S13.DocMind Refactoring Plan (Comprehensive Delta Review)

## 1. Current Delta Snapshot
- **Bootstrap still bypasses Koan auto-registration** – `Program.cs` loads the DocMind assembly into the cache but never calls `AddKoanModules`, so `IKoanAutoRegistrar` implementations (including `DocMindRegistrar`) do not run and hosted workers/options validation stay dormant unless consumers wire them manually. 【F:samples/S13.DocMind/Program.cs†L1-L23】
- **Pipeline orchestration collapses multiple stages into one worker pass** – `DocumentAnalysisPipeline` dequeues work with a bespoke channel, replays every stage in one shot, and only updates the work item at completion. Stage-specific retries, `Deduplicate`/`GenerateEmbeddings` transitions, and targeted resumptions described in the blueprint remain unimplemented. 【F:samples/S13.DocMind/Infrastructure/DocumentAnalysisPipeline.cs†L37-L345】【F:samples/S13.DocMind/Infrastructure/DocumentPipelineQueue.cs†L1-L216】
- **Queue diagnostics lack persistence and state awareness** – Work items live solely in-memory, `ScheduleRetryAsync` reuses the same `DocumentWorkItem` without updating stage/status telemetry, and requeue helpers always restart at `ExtractText`, preventing diagnostics from requesting stage-specific resumes. 【F:samples/S13.DocMind/Infrastructure/DocumentPipelineQueue.cs†L12-L221】【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L134-L197】
- **Repositories and discovery services still perform collection scans** – Timeline, insights, and aggregation services hydrate entire tables through `Entity<T>.All()` or ad-hoc filters, contradicting the proposal’s repository-driven, server-side projection strategy and limiting scalability. 【F:samples/S13.DocMind/Infrastructure/Repositories/ProcessingEventRepository.cs†L12-L45】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L28-L149】
- **Vector and semantic profile alignment remains partial** – Chunk embeddings persist to the adapter, but template suggestions still execute cosine similarity in-process after loading every embedding/profile, and no bootstrapper ensures semantic profile vectors exist. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L94-L209】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L34】
- **Operational hardening is unfinished** – Boot reports list configuration but omit health checks, AI/vector readiness probes, or telemetry hooks, and there is no automated coverage guarding the rebuilt pipeline. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L40-L86】

## 2. Layered Remediation Strategy

### Layer A – Reactivate Bootstrap & Configuration Enforcement
1. **Adopt Koan module loading** – Replace the `AssemblyCache` call with `builder.Services.AddKoanModules(typeof(DocMindRegistrar).Assembly);` so the auto-registrar runs and hosted services/options validation activate on startup.
2. **Centralize options validation** – Keep `AddKoanOptions<DocMindOptions>` but add `ValidateDataAnnotations()`/custom validators to fail fast on malformed storage/processing settings, mirroring the infrastructure guidance.
3. **Expose boot readiness checks** – Extend `DocMindRegistrar.Describe` to run storage/vector/AI health probes and emit actionable warnings instead of static settings dumps.

### Layer B – Processing Topology & Stage Semantics
1. **Introduce the proposal’s worker loop** – Swap the channel queue for the polling `DocumentProcessingWorker` pattern, persisting work status between iterations so retries and diagnostics have durable insight.
2. **Model stage progression explicitly** – Teach the worker to persist `DocumentWorkItem` (or equivalent) state, emit `Deduplicate`/`GenerateEmbeddings` events, and update `SourceDocument.Summary` after each completed stage.
3. **Targeted retry support** – Allow diagnostics uploads to requeue a specific stage by persisting last successful stage/status, honoring that when scheduling retries instead of always restarting at extraction.

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
