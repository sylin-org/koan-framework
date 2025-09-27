# S13.DocMind Next Steps (Post-Delta Alignment)

## Iteration 0 – Reactivate Bootstrapping & Configuration
1. **Wire Koan modules** – Update `Program.cs` to call `.AddKoanModules(typeof(DocMindRegistrar).Assembly)` so the registrar, hosted services, and options validation execute on startup.
2. **Bind and validate options** – Use `AddKoanOptions<DocMindOptions>` (or equivalent) to bind the `DocMind` section, enable `ValidateOnStart`, and fail fast when queue/storage/AI settings are invalid.
3. **Enrich boot diagnostics** – Extend `DocMindRegistrar.Describe` with queue sizing, storage/vector readiness, and AI model summaries to match the infrastructure blueprint.

## Iteration 1 – Stage Semantics & Worker Realignment
1. **Adopt polling worker** – Replace the channel queue with the proposal’s polling `DocumentProcessingWorker`, keeping retry/backoff behaviour but exposing a simpler orchestration surface.
2. **Emit full stage telemetry** – Persist `DocumentProcessingEvent` entries for `Deduplicate`, `GenerateEmbeddings`, and aggregation stages; update `SourceDocument.Summary` accordingly.
3. **Respect targeted retries** – Allow diagnostics to requeue the requested stage and persist failure exhaustion events before marking work complete.

## Iteration 2 – Data Access & Aggregations
1. **Repository-driven queries** – Refactor diagnostics and insight services to rely on repository helpers that issue server-side filters instead of `Entity<T>.All()` calls.
2. **Insight collections** – Introduce `InsightCollection` / `SimilarityProjection` aggregates and background refresh so dashboards pull precomputed rollups.
3. **Semantic bootstrap** – Expand vector bootstrapping to ensure semantic profile embeddings exist (or are regenerated) before suggestion flows run.

## Iteration 3 – AI & Vector Completion
1. **Vector-powered suggestions** – Replace manual cosine similarity with adapter-backed vector search (`Vector<SemanticTypeEmbedding>.SearchAsync`) and record `GenerateEmbeddings` timeline events.
2. **Model readiness checks** – Validate configured AI models during boot, emitting actionable warnings and fallbacks when providers are missing.
3. **Chunk ↔ insight sync** – When AI returns chunk references, populate `DocumentChunk.InsightRefs` and stage metrics so summaries stay consistent.

## Iteration 4 – Operational Hardening & UX
1. **Health checks & telemetry** – Add Koan health checks for storage/vector/AI adapters and wrap pipeline stages with OpenTelemetry spans.
2. **Automated coverage** – Introduce integration tests (happy path, retry exhaustion, vision-disabled) using Koan in-memory adapters.
3. **Docs & tooling refresh** – Update README/setup scripts to describe the new bootstrap flow, feature toggles, and operational playbooks.
