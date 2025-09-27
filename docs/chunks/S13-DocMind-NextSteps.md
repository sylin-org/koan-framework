# S13.DocMind Next Steps (Aligned to Updated Refactoring Plan)

## Iteration 0 – Bootstrap Optimization & Health Probes
1. Leverage minimal `AddKoan()` pattern in `Program.cs` which automatically discovers and initializes `DocMindRegistrar` without manual assembly loading or explicit service registration. 【F:samples/S13.DocMind/Program.cs†L1-L9】
2. Move options configuration into `DocMindRegistrar.Initialize()` following "Reference = Intent" where modules manage their own dependencies. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L24-L25】
3. Extend `DocMindRegistrar.Describe` to run storage/vector/AI readiness checks and surface them in the boot report alongside existing configuration.

## Iteration 1 – Stage-Oriented Worker & Targeted Retries
1. Replace the channel-backed queue with the polling worker plus persisted job ledger so stage/status survive restarts and retries resume at the correct boundary. 【F:samples/S13.DocMind/Infrastructure/DocumentPipelineQueue.cs†L10-L200】【F:samples/S13.DocMind/Infrastructure/DocumentAnalysisPipeline.cs†L36-L319】
2. Break `ProcessWorkAsync` into discrete handlers that emit `DocumentProcessingEvent` entries before/after each stage (`Deduplicate`, `GenerateEmbeddings`, `Aggregate`) while updating `SourceDocument.Summary` incrementally. 【F:samples/S13.DocMind/Infrastructure/DocumentAnalysisPipeline.cs†L78-L320】
3. Update diagnostics APIs so requeue requests persist the requested stage and emit queue events instead of always restarting at extraction. 【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L169-L195】

## Iteration 2 – Repository-Driven Data Access
1. Introduce repositories for documents, chunks, insights, and processing events that push filters/pagination server-side, removing `Entity<T>.All()` usage in discovery services. 【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L28-L149】
2. Rebuild aggregation feeds on top of the new repositories and materialized insight collections to avoid recomputing metrics per request.
3. Move the processing timeline endpoint to the repository API with windowing so MCP tooling can page through events. 【F:samples/S13.DocMind/Infrastructure/Repositories/ProcessingEventRepository.cs†L12-L45】

## Iteration 3 – AI & Vector Completion
1. Swap template suggestion scoring to `Vector<SemanticTypeEmbedding>.SearchAsync`, capturing adapter diagnostics and emitting `GenerateEmbeddings` timeline events while mirroring S5.Recs’ approach of checking `Vector<T>.IsAvailable`, invoking `Vector<T>.Search`, and falling back to repository queries when vectors are offline. 【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L124-L195】【F:samples/S5.Recs/Services/RecsService.cs†L96-L149】
2. Expand vector bootstrapping to ensure semantic profile embeddings are created/refreshed alongside chunk indices and expose readiness stats similar to S5.Recs’ vector seeding utilities. 【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L34】【F:samples/S5.Recs/Services/SeedService.cs†L288-L303】
3. Capture AI model readiness and latency metrics during boot and processing, wiring them into processing events for diagnostics.

## Iteration 4 – Operational Hardening & Quality
1. Add Koan health checks for storage, vector adapters, AI providers, and queue backlog thresholds; surface results through MCP/UI endpoints. 【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L46-L80】
2. Wrap pipeline stages with OpenTelemetry spans and persist duration/token metrics in `DocumentProcessingEvent` records.
3. Author integration tests covering upload→completion, targeted retries, and vector-disabled fallbacks using in-memory providers to guard the rebuilt pipeline. 【e8959d†L1-L11】

## Immediate Spikes & Research Tasks
1. Prototype the persisted worker ledger design and migration story before swapping out the channel queue to de-risk data loss.
2. Draft repository queries/aggregations for dashboards and timeline endpoints, validating them against sample data to ensure the discovery refactor lands smoothly.
3. Evaluate adapter search capabilities (Weaviate or alternative) and outline graceful-degradation paths when vector services are offline, borrowing the S5.Recs query/fallback layering as a reference implementation. 【F:samples/S5.Recs/Services/RecsService.cs†L96-L149】
4. Scope the health-check/telemetry wiring and DocMind-focused test harness so they can ship alongside the worker refactor instead of trailing it.
