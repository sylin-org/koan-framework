# S13.DocMind Next Steps (Proposal Delta)

## Discovery Projection Hardening
1. **Add change detection.** Introduce refresh throttling and save suppression so `DocumentDiscoveryProjectionBuilder` skips rewrites when content is unchanged and avoids whole-table scans for small deltas.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L1-L220】
2. **Validate at scale.** Build representative fixtures/tests that exercise large document/insight sets to confirm overview, collection, and queue projections stay accurate as data grows.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L1-L150】
3. **Surface freshness metadata.** Ensure discovery endpoints and docs highlight `RefreshedAt`, paging, and cache behavior so UI/MCP clients can reason about staleness without recomputing aggregates.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L20-L80】

## Vector Readiness & Diagnostics
1. **Bootstrap semantic profiles.** Extend `DocumentVectorBootstrapper` (or add a companion worker) to seed and refresh semantic profile embeddings alongside chunk vectors.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L60】
2. **Expose adapter health.** Publish vector readiness/latency via the registrar and diagnostics queue so operators see when the system falls back to cosine mode.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L90】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L500-L620】
3. **Test vector modes.** Add unit/integration coverage for vector success, fallback, and degraded paths (including telemetry expectations) to keep suggestion behavior stable.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L120-L320】

## Replay & Operational Quality
1. **Finish replay flows.** Deliver the stage-targeted retry CLI/APIs with concurrency guards and correlation IDs so MCP tooling can drive resumptions using the new ledger telemetry.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L120】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L52】
2. **Register health & telemetry.** Wire storage/vector/model probes and OpenTelemetry exporters into `DocMindRegistrar` to match the infrastructure playbook.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L40-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L90】
3. **Stand up DocMind tests.** Create the dedicated unit/integration suites with fake AI/vector providers and pipeline smoke tests, wiring them into CI as the testing plan outlines.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L110】

## Immediate Spikes
- **Projection load test:** Capture metrics from replaying sample data through `DocumentDiscoveryProjectionBuilder` to size incremental refresh work.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L1-L220】
- **Semantic profile audit:** Inventory existing profiles/embeddings to scope bootstrap coverage and fallback behavior.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L60】
- **Replay tooling UX review:** Prototype the CLI/API surface for stage-targeted resumes before wiring into diagnostics to ensure operator workflows make sense.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L120】
