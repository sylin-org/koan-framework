# S13.DocMind Next Steps (Proposal Delta)

## Observability & Telemetry Instrumentation
1. **Register OpenTelemetry exporters and spans.** Update the registrar/startup to add tracing/metrics exporters and wrap the processing stages with spans so the compose/collector profile in the proposal can surface queue depth and latency dashboards.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L74-L77】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L52】
2. **Publish per-stage metrics through diagnostics.** Extend `ProcessingDiagnosticsService` to expose counters (processed, failed, average latency, embedding throughput) that Grafana can consume alongside vector/discovery readiness snapshots.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L74-L77】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L186-L220】

## Automated Validation & CI Coverage
1. **Build the fake-provider harness.** Create Mongo/AI test doubles so unit and integration projects can exercise upload→completion end to end without external services, replacing the skipped replay test with deterministic coverage.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L59】【F:tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L8-L22】
2. **Wire regression runs into CI.** Add workflows (or update existing ones) that run the DocMind unit/integration suites plus CLI smoke commands, aligning with the release checklist expectations.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L82】

## Discovery Projection Performance Guardrails
1. **Replay representative corpora.** Use the validation CLI to drive large datasets through the refresher, measuring refresh duration/backlog so we can tune thresholds before scaling; today a refresh still scans every document/insight when change detection fires.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L78】
2. **Surface projection accuracy diagnostics.** Add comparisons (document counts, insight totals, queue age) between the cached projection and live collections so operators can detect drift before trusting dashboards.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L78-L83】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L186-L220】

## Immediate Spikes
- **Telemetry wiring blueprint:** Prototype the OpenTelemetry configuration locally to confirm which exporters/instrumentation packages we need before committing changes.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L74-L77】
- **In-memory pipeline fixture:** Sketch the fake AI/storage fixtures required to un-skip the replay test and cover a full happy-path document.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L5-L59】【F:tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L8-L22】
- **Projection drift dashboarding:** Draft the additional diagnostics payloads (counts, freshness deltas) needed for reliable discovery dashboards before implementing them in the service layer.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L78】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L186-L220】
