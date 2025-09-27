# S13.DocMind Refactoring Plan (Proposal Delta)

## Remaining Gaps
- **Projection refresh path is still brute-force.** `DocumentDiscoveryProjectionBuilder` rebuilds the entire projection on every refresh, loading all documents/insights and persisting even when content is unchanged, so large datasets will suffer and freshness metadata remains unverified.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L1-L220】
- **Vector readiness stops at adapter calls.** Suggestions now use the vector adapter, but semantic profile embeddings are never bootstrapped, and diagnostics/registrar outputs still hide adapter health and fallback state.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L120-L320】【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L60】
- **Replay tooling, health checks, and tests remain missing.** The worker/diagnostics expose telemetry, yet no CLI/test coverage exercises stage-targeted resumes, and the registrar still omits the health/telemetry wiring the proposal requires.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L120】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L90】【docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】

## Refactoring Streams

### 1. Discovery Projection Hardening
1. **Throttle refresh work.** Add change detection or incremental/batched queries so `DocumentDiscoveryProjectionBuilder` skips redundant saves and avoids loading the entire corpus on every invocation.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L1-L220】
2. **Validate projection output.** Create representative fixtures/tests that exercise large document/insight sets to confirm collection summaries, queue projections, and overview metrics stay accurate at scale.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L1-L150】
3. **Surface freshness metadata.** Ensure discovery endpoints expose `RefreshedAt`/paging hints so UI/MCP clients can react to stale data without recomputing aggregates.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L20-L80】

### 2. Vector Readiness & Diagnostics
1. **Bootstrap semantic profiles.** Extend the existing vector bootstrapper (or a companion worker) to seed and refresh semantic profile embeddings, mirroring the chunk bootstrap logic.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L60】
2. **Publish adapter health.** Emit readiness/latency metrics through the registrar and diagnostics queue so operators can see when suggestions fall back to cosine mode.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L90】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L500-L620】
3. **Backstop with tests.** Add unit/integration coverage for vector success, fallback, and degraded modes (including telemetry expectations) to keep future changes honest.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L120-L320】

### 3. Replay Tooling & Operational Quality
1. **Deliver replay CLI/APIs.** Finish the stage-targeted resume endpoints/commands that consume the new ledger telemetry, including concurrency guards and correlation IDs for MCP tooling.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L1-L120】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L1-L52】
2. **Wire health & telemetry.** Register the storage/vector/model health probes and OpenTelemetry exporters outlined in the infrastructure plan so the sample surfaces readiness and tracing data.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L40-L112】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L90】
3. **Stand up DocMind test suites.** Add the dedicated unit/integration projects with fake AI/vector providers and pipeline smoke tests to satisfy the testing blueprint.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L110】

## Delivery Order
1. **Iteration 2 wrap-up – Discovery projection validation.** Land refresh throttling, save suppression, and projection tests so analytics endpoints scale.
2. **Iteration 3 – Vector readiness.** Bootstrap semantic profiles, surface adapter health, and add vector/fallback test coverage.
3. **Iteration 4 – Replay & operations.** Ship replay tooling alongside health checks, telemetry wiring, and DocMind-focused automated tests.

Each iteration builds on the durable ledger delivered earlier while closing the remaining discovery, vector, and operational deltas.
