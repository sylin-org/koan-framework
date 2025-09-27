# S13.DocMind Refactoring Plan (Proposal Delta)

## Completion Snapshot
- **Estimated completion:** ~92% of the proposal scope. The stage-aware worker runs a dedicated embeddings stage against the durable ledger, discovery refreshes queue through `DocumentDiscoveryRefreshService`, vector bootstrap audits semantic profiles, the operator CLI exercises diagnostics endpoints, and registrar health checks surface vector/discovery readiness as promised.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L1-L760】【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L1-L180】【F:samples/S13.DocMind.Tools/Program.cs†L1-L140】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L1-L120】
- **Outstanding delta:** We still need to validate incremental discovery accuracy under load, wire replay/CLI automation into CI, add vector smoke coverage, instrument OpenTelemetry exporters, and activate the DocMind unit/integration suites with fake providers.【F:samples/S13.DocMind/Services/DocumentDiscoveryRefresher.cs†L1-L160】【tests/S13.DocMind.UnitTests/DocumentDiscoveryRefreshServiceTests.cs†L1-L80】【docs/chunks/S13-DocMind/07_testing_ops.md†L1-L67】

## Remaining Delta

### 1. Validate discovery projections at scale
- **Exercise the incremental path.** Change detection, the async scheduler, and the validation endpoint/CLI now prevent redundant refreshes, but no load or regression tests have validated projection accuracy under production-sized datasets.【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L1-L180】【F:samples/S13.DocMind/Services/DocumentDiscoveryRefresher.cs†L1-L160】 Capture representative corpora, replay them through the validator, and tune scheduler throttles based on measured duration/throughput.
- **Expose scheduler insights.** The refresh service tracks pending counts, totals, and last-run metadata; surface those metrics through diagnostics/dashboards so operators can monitor backlog growth once validation lands.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L20-L360】

### 2. Automate replay & operator tooling
- **Wire the CLI/MCP flows.** The command-line tool can now hit `/processing/config`, `/processing/replay`, and `/processing/discovery/validate`; update operator playbooks and ship MCP bindings so resumability is turnkey across hosts.【F:samples/S13.DocMind.Tools/Program.cs†L1-L140】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L101】
- **Add replay regression tests.** Light up the pending integration tests (and add fake-host unit coverage) so replay requests against staged failures prove the ledger resumes at the requested stage after future refactors.【tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L1-L26】【docs/chunks/S13-DocMind/07_testing_ops.md†L40-L67】

### 3. Harden vector resilience
- **Cover adapter/fallback paths in CI.** Vector readiness telemetry exists and unit tests cover readiness snapshots, but automated smoke tests still need to exercise adapter availability, bootstrap backfills, and cosine fallback accuracy.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L1-L180】【tests/S13.DocMind.UnitTests/DocMindVectorHealthTests.cs†L1-L34】 Add fake-adapter smoke tests to lock in both success and degraded modes.
- **Visualise vector health.** Feed `DocMindVectorHealth` snapshots into diagnostics responses and dashboards so operators can see missing profiles/latency at a glance.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L1-L520】【samples/S13.DocMind/wwwroot/openapi.json†L1-L520】

### 4. Wire telemetry & DocMind test suites
- **Add OpenTelemetry exporters.** Health checks now exist, but traces/metrics are still absent. Hook the sample into Koan's OpenTelemetry pipeline per the infrastructure blueprint.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L40-L112】
- **Activate DocMind unit/integration projects.** The test projects are checked in; implement fake-provider pipeline tests and run them in CI so the refactored worker, discovery scheduler, and vector bootstrap flows stay guarded.【tests/S13.DocMind.UnitTests/DocumentDiscoveryRefreshServiceTests.cs†L1-L80】【tests/S13.DocMind.IntegrationTests/ReplayWorkflowTests.cs†L1-L26】

## Delivery Order
1. **Discovery validation & scheduler tuning.** Close the remaining analytics delta by validating incremental refresh accuracy/performance and exposing scheduler metrics.
2. **Replay automation & vector resilience.** Deliver the CLI/tests for replay flows while locking vector readiness into CI via fake-adapter coverage.
3. **Telemetry & DocMind test suites.** Wire OpenTelemetry exporters and land the dedicated unit/integration projects to finish the proposal’s operational guarantees.
