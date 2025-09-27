# S13.DocMind Next Steps (Proposal Delta)

## Discovery Validation & Scheduler Tuning
1. **Replay captured datasets.** Use the validation endpoint/CLI to drive the scheduler with production-sized corpora, confirming incremental accuracy and measuring refresh duration/backlog growth.【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L1-L180】【F:samples/S13.DocMind/Services/DocumentDiscoveryRefresher.cs†L1-L160】
2. **Publish scheduler metrics.** Extend diagnostics/dashboards with the tracked pending counts, totals, and durations so operators can spot refresh drift in real time.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L20-L360】

## Replay Automation & Operator Tooling
1. **Ship CLI/MCP bindings.** Finalise documentation and MCP wiring around the new CLI so operators and agents can queue replays/validations without custom scripts.【F:samples/S13.DocMind.Tools/Program.cs†L1-L140】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L101】
2. **Add replay tests.** Enable the pending integration test (and add fake-host unit tests) to ensure the ledger resumes at the requested stage across upgrades.【tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L1-L26】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L40-L67】

## Vector Resilience Coverage
1. **Test adapter/fallback paths.** Add fake adapter smoke tests that validate semantic bootstrap, vector search, and cosine fallback behavior in CI.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L180】【tests/S13.DocMind.UnitTests/DocMindVectorHealthTests.cs†L1-L34】
2. **Surface health snapshots.** Visualise the new `DocMindVectorHealth` metrics inside diagnostics dashboards so operators can monitor missing profiles and latency trends.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L520】

## Telemetry & Test Suites
1. **Wire OpenTelemetry exporters.** Register tracing/metrics exporters per the infrastructure guide so DocMind telemetry flows into Koan observability pipelines.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L40-L112】
2. **Activate DocMind unit/integration tests.** Flesh out the new projects with fake-provider coverage and hook them into CI to guard the refactored pipeline.【tests/S13.DocMind.UnitTests/DocumentDiscoveryRefreshServiceTests.cs†L1-L80】【tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L1-L26】

## Immediate Spikes
- **Scheduler load rehearsal:** Capture a representative dataset and replay it through the validation endpoint to benchmark duration/backlog thresholds before codifying alerts.【F:samples/S13.DocMind/Services/DocumentDiscoveryRefresher.cs†L1-L160】
- **Replay DX design:** Document the CLI/MCP flows to ensure stage targeting, payload shape, and error messaging meet operator expectations.【F:samples/S13.DocMind.Tools/Program.cs†L1-L140】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L101】
- **Vector health dashboards:** Sketch UI/diagnostic payloads that visualise the new `DocMindVectorHealth` snapshot so operators immediately see degraded modes.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L520】
