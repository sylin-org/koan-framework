# S13.DocMind Refactoring Plan (Post-Bedrock Implementation Review)

## 1. Delta Checkpoint
- **Module wiring stalled** – `Program.cs` never loads Koan modules, so `DocMindRegistrar` (and the hosted pipeline/vector workers it registers) never activate. Options binding, validators, and boot report contributions remain inert even though the registrar exists. 【F:samples/S13.DocMind/Program.cs†L1-L19】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L17-L49】
- **Pipeline stages drift from the proposal** – The channel-backed `DocumentPipelineQueue` is still the only orchestration mechanism even though the blueprint called for a simple polling worker. Several declared stages (`Deduplicate`, `GenerateEmbeddings`) are never emitted, retries always restart at `ExtractText`, and embeddings are generated opportunistically without stage telemetry. 【F:samples/S13.DocMind/Infrastructure/DocumentPipelineQueue.cs†L1-L214】【F:samples/S13.DocMind/Models/SourceDocument.cs†L176-L205】【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L120-L188】
- **Data access remains brute-force** – Diagnostics and insight services hydrate whole collections via `Entity<T>.All()` and in-memory LINQ, contradicting the repo/aggregation strategy outlined in the proposal and threatening scalability once documents grow. 【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L22-L140】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L30-L137】
- **AI & vector integration is only partially realized** – Vision/text synthesis now call AI providers, but the system still relies on cosine similarity over in-memory embeddings, skips semantic profile bootstrapping, and never records stage-specific embedding events for diagnostics. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L82-L164】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L36】
- **Operational surface incomplete** – Boot reports expose only three settings, no health checks validate storage/vector/model readiness, and there is no automated coverage to guard the rebuilt pipeline. Retry tooling always requeues stage `ExtractText`, ignoring targeted restarts requested by diagnostics. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L35-L49】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L105-L137】

## 2. Layered Remediation Strategy

### Layer A – Bootstrap & Configuration Activation
1. **Load the module**: Update `Program.cs` to chain `.AddKoanModules(typeof(DocMindRegistrar).Assembly)` (or equivalent) so the registrar, hosted services, and options validation execute.
2. **Unify options binding**: Replace ad-hoc option fetches with `AddKoanOptions<DocMindOptions>` or `AddOptions<DocMindOptions>().BindConfiguration("DocMind").ValidateOnStart()` so pipelines, storage, and AI services read consistent configuration across environments.
3. **Enhance boot diagnostics**: Extend `DocMindRegistrar.Describe` to surface queue sizing, provider availability, AI model readiness, and feature flags to mirror the infrastructure blueprint.

### Layer B – Domain & Stage Semantics
1. **Honor full stage model**: Emit `DocumentProcessingEvent` entries (and update `SourceDocument.Summary`) for `Deduplicate`, `GenerateEmbeddings`, and profile aggregation stages. Ensure retry/resume logic respects the requested stage instead of always restarting at `ExtractText`.
2. **Stage-aware work items**: Replace or wrap `DocumentPipelineQueue` with the blueprint’s polling worker so concurrency, retries, and stage progression are declarative and traceable. Persist work state when retries exhaust to support diagnostics.
3. **Chunk ↔ insight linkage**: When insights reference chunks, update `DocumentChunk.InsightRefs` to keep summary references in sync and populate stage durations/metrics for downstream dashboards.

### Layer C – Data Access & Aggregations
1. **Repository-first queries**: Expand repository helpers (documents, chunks, insights, processing events) to expose targeted projections (pending queue items, per-document timelines, profile aggregations) using server-side filters instead of `All()` calls.
2. **Insight collections**: Introduce the `InsightCollection`/`SimilarityProjection` aggregates from the proposal so discovery dashboards and MCP tools can query precomputed rollups instead of recomputing metrics on each request.
3. **Semantic profile bootstrap**: Extend the vector bootstrapper (or add a dedicated initializer) to seed semantic type embeddings and ensure both chunk/profile classes exist before suggestions run.

### Layer D – AI & Vector Integration
1. **Vector-backed suggestions**: Replace the in-memory cosine similarity loop with Koan vector adapter queries (`Vector<SemanticTypeEmbedding>.SearchAsync`) so template suggestions scale and align with the Weaviate-based design.
2. **Embedding stage telemetry**: Record explicit `GenerateEmbeddings` events (metrics: vector count, adapter latency) and surface failures with retry semantics so diagnostics expose vector readiness.
3. **Model capability checks**: On startup, verify configured AI models exist (or expose actionable boot warnings) and add graceful fallbacks when providers are absent, as called out in the proposal’s infrastructure guidance.

### Layer E – Diagnostics, Experience & Ops
1. **Retry targeting**: Allow diagnostics to requeue the specific stage that failed, capturing correlation IDs and audit events so operators can restart from insight synthesis without rerunning extraction unnecessarily.
2. **Health checks & telemetry**: Add Koan health checks for storage path, vector adapter, and AI provider; emit OpenTelemetry spans around AI/vision calls and queue processing to match the observability commitments.
3. **Automated coverage**: Introduce integration tests using Koan’s in-memory providers that cover upload → completion, retry exhaustion, and vision-disabled paths to protect the rebuilt pipeline.

## 3. Execution Roadmap (Break & Rebuild Friendly)
1. **Reactivate the module (Layer A)** – Wire up `Program.cs`, tighten options, and enrich boot diagnostics so hosted services and validators actually run.
2. **Repair stage orchestration (Layer B)** – Swap in the polling worker, honor all processing stages, and align timeline/summaries with the blueprint’s expectations.
3. **Stabilize data access (Layer C)** – Move diagnostics/insights onto repository-driven projections and seed vector/semantic data.
4. **Complete AI/vector alignment (Layer D)** – Implement adapter-backed similarity, embed telemetry, and enforce model readiness checks.
5. **Harden operations (Layer E)** – Deliver targeted retries, health checks, telemetry, and automated tests before refreshing docs/tooling.

Each milestone should leave the sample runnable (with temporary feature toggles if needed) while progressively restoring parity with the S13.DocMind proposal.
