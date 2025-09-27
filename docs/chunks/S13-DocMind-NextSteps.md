# S13.DocMind Next Steps (Aligned to Updated Refactoring Plan)

## Iteration 0 – Bootstrap Optimization & Health Probes
1. Leverage minimal `AddKoan()` pattern in `Program.cs` which automatically discovers and initializes `DocMindRegistrar` without manual assembly loading or explicit service registration. 【F:samples/S13.DocMind/Program.cs†L1-L9】
2. Move options configuration into `DocMindRegistrar.Initialize()` following "Reference = Intent" where modules manage their own dependencies. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L24-L25】
3. Extend `DocMindRegistrar.Describe` to run storage/vector/AI readiness checks and surface them in the boot report alongside existing configuration.

## Iteration 1 – Stage-Oriented Worker & Targeted Retries
1. Replace the channel-backed queue with the polling worker pattern, persisting work state per document/stage and honoring concurrency from options. 【F:samples/S13.DocMind/Infrastructure/DocumentPipelineQueue.cs†L1-L221】
2. Emit explicit `Deduplicate`, `GenerateEmbeddings`, and aggregation events from `DocumentAnalysisPipeline`, updating `SourceDocument.Summary` after each stage. 【F:samples/S13.DocMind/Infrastructure/DocumentAnalysisPipeline.cs†L37-L345】
3. Update diagnostics APIs so requeue requests resume from the requested stage instead of always restarting at extraction. 【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L172-L197】

## Iteration 2 – Repository-Driven Data Access
1. Introduce repositories for documents, chunks, insights, and processing events that push filters/pagination server-side, removing `Entity<T>.All()` usage in discovery services. 【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L28-L149】
2. Rebuild aggregation feeds on top of the new repositories and materialized insight collections to avoid recomputing metrics per request.
3. Move the processing timeline endpoint to the repository API with windowing so MCP tooling can page through events. 【F:samples/S13.DocMind/Infrastructure/Repositories/ProcessingEventRepository.cs†L12-L45】

## Iteration 3 – AI & Vector Completion
1. Swap template suggestion scoring to `Vector<SemanticTypeEmbedding>.SearchAsync`, capturing adapter diagnostics and emitting `GenerateEmbeddings` timeline events. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L94-L209】
2. Expand vector bootstrapping to ensure semantic profile embeddings are created/refreshed alongside chunk indices. 【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L34】
3. Capture AI model readiness and latency metrics during boot and processing, wiring them into processing events for diagnostics.

## Iteration 4 – Operational Hardening & Quality
1. Add Koan health checks for storage, vector adapters, AI providers, and queue backlog thresholds; surface results through MCP/UI endpoints.
2. Wrap pipeline stages with OpenTelemetry spans and persist duration/token metrics in `DocumentProcessingEvent` records.
3. Author integration tests covering upload→completion, targeted retries, and vector-disabled fallbacks using in-memory providers to guard the rebuilt pipeline.
