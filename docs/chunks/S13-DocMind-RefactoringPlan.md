# S13.DocMind Refactoring Plan (Proposal Delta)

## Completion Snapshot
- **Estimated completion:** ~96% of the proposal scope. The stage-aware worker now drives every document through a durable job ledger with stage telemetry, refreshing discovery projections as work completes and updating queue analytics for operators.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L156】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingJobRepository.cs†L12-L102】【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L12-L173】
- **Delivered capabilities:** Vector-backed template suggestions fall back gracefully when adapters are offline, the CLI exposes replay/config/validation automation, and diagnostics project vector/discovery readiness for operators.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L128-L208】【F:samples/S13.DocMind.Tools/Program.cs†L18-L100】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L15-L220】

## Remaining Delta

### 1. Observability & telemetry instrumentation
- Wire the sample into Koan's OpenTelemetry pipeline and emit spans/metrics around each processing stage so the compose profile and dashboards envisioned in the proposal can light up; the registrar currently registers health checks only and never adds exporters or tracing.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L74-L77】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L52】
- Capture per-stage counters (queue depth, stage latency, embedding throughput) and publish them through the diagnostics endpoints to match the Grafana dashboard expectations from the plan.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L74-L77】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L186-L220】

### 2. Automated validation & CI coverage
- Stand up the in-memory/fake-provider harness described in the testing plan so unit and integration suites exercise the upload→completion pipeline without external dependencies; the only integration test remains skipped pending real infrastructure.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L67】【F:tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L8-L22】
- Hook those suites into CI (including CLI smoke coverage) to deliver the regression automation required by the proposal’s release checklist.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L82】

### 3. Discovery projection performance guardrails
- The projection builder still performs full collection scans when change detection triggers a refresh, so we need representative dataset replays and regression assertions to prove the incremental scheduler holds up at DocMind-scale volumes.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L78】
- Extend diagnostics to capture projection accuracy/lateness comparisons (e.g., sample counts vs. live collections) so operators can trust the cached analytics before rolling out broadly.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L78-L83】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L15-L220】

## Delivery Order
1. Observability & telemetry instrumentation.
2. Automated validation & CI coverage.
3. Discovery projection performance guardrails.
