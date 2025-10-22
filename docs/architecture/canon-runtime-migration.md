# Canon Runtime Migration Plan

## Contract

- **Problem Statement:** Replace the legacy canon stack with the new domain runtime—breaking changes are expected, and all dependent projects must realign.
- **Inputs:** Canon domain project (`Koan.Canon.Domain`), connector updates, sample migrations, operational tooling.
- **Outputs:** A production-ready canon runtime, new controller surfaces, and updated samples aligned with the rebuilt patterns.
- **Success Criteria:** All samples, tests, and adapters run exclusively on the new domain runtime; legacy projects are removed from the active graph once the cutover lands.
- **Error Modes:** Incomplete runtime features, missing adapter coverage, and insufficient developer guidance slowing adoption of the new patterns.

## Current State

- Legacy canon assemblies have been isolated under `legacy/` and continue to serve existing samples.
- `Koan.Canon.Domain` delivers domain models, pipeline contracts, and unit tests.
- Canon lifecycle and readiness are represented through the new `CanonState` model (`CanonLifecycle`, `CanonReadiness`, `CanonState`).
- Documentation and proposals outline intentions but no production runtime implementation exists yet.
- `Koan.Canon.Web` exposes controller surfaces powered by `ICanonRuntime`, replacing the legacy web layer.

## Milestones

**Status as of 2025-10-05**: M1 ✅ Complete | M2 ✅ Complete | M3 ⏳ Pending | M4 ⏳ Pending | M5 ⏳ Pending

1. **M1 — Runtime Core** ✅ **COMPLETE**

   - Implement the execution engine that drives stage behaviors using `CanonPipelineContext`.
   - Deliver persistence adapters for canon entities and metadata using Koan entity statics.
   - Provide smoke tests that execute a two-stage pipeline end-to-end.

2. **M2 — Web & API Surfaces**

   - Rebuild canon controllers on top of the new runtime following MVC conventions.
   - Generate OpenAPI descriptions and update API reference docs.
   - Switch S8.Canon to the new endpoints and delete its legacy wiring.
   - ✅ Controller package (`Koan.Canon.Web`) registered with auto-discovery (Oct 2025).

3. **M3 — Adapter Modernization**

   - Port critical adapters (Dapr runtime connector, projection services) to consume the new runtime contracts.
   - Validate provider capability negotiation (streaming vs. paged) and document the expected behavior for the new stack.
   - Add integration tests for each adapter path.

4. **M4 — Sample & Test Migration**

   - Update all canon-enabled samples (S8, S9, S14) to reference `Koan.Canon.Domain` instead of legacy projects.
   - Expand automated tests to cover multi-stage pipelines, observers, and failure scenarios.
   - Remove legacy sample instructions and replace them with the new workflow.

5. **M5 — Operational Cutover**
   - Ship observability hooks (metrics, structured logs, health checks) for the runtime.
   - Provide tooling to seed the new runtime with any required canonical data.
   - Publish a cutover guide and remove legacy assemblies from the supported surface.
   - Rename the `Koan.Canon.Domain` assembly/package to `Koan.Canon` once all downstream consumers have migrated.

### M1 & M2 Completion Notes (2025-10-05)

**M1 Achievements**:

- Full runtime pipeline execution with 6 phases (Intake → Validation → Aggregation → Policy → Projection → Distribution)
- 21 passing domain tests covering entities, state, metadata, and runtime
- `IServiceProvider` support in `CanonPipelineContext` for contributor dependency injection
- `EntityType` added to `CanonizationRecord` for replay filtering
- Async observer pattern (`BeforePhaseAsync`, `AfterPhaseAsync`, `OnErrorAsync`)
- In-memory replay with configurable capacity

**M2 Achievements**:

- Generic canon controllers with auto-discovery (`CanonEntitiesController<T>`)
- Discovery endpoint (`/api/canon/models`) exposing pipeline metadata
- Admin operations (`GET /api/canon/admin/records`, `POST /api/canon/admin/{slug}/rebuild`)
- Comprehensive option parsing from headers (`X-Canon-Origin`, `X-Correlation-ID`, `X-Canon-Tag-*`) and query (`?origin=`, `?forceRebuild=`, `?tag.key=value`)
- Correlation ID auto-detection (X-Correlation-ID → X-Request-ID → TraceIdentifier)
- 1 passing web controller test

**Next Steps**: Begin M3 (Adapter Modernization) and M4 (Sample Migration - S8.Canon requires complete rewrite for new runtime)

## Edge Cases & Mitigations

- **Runtime Completeness:** Missing stage behaviors or observers block adoption—track them as must-fix items before cutover.
- **Provider Gaps:** For providers lacking streaming, document and enforce paging limits to prevent memory pressure.
- **Failure Isolation:** Ensure stage crashes do not corrupt downstream state; require idempotent writes and compensation hooks.
- **Sample Parity:** Each migrated sample must ship with working instructions using the new runtime; delete outdated guidance during the same milestone.
- **Developer Onboarding:** Update quick starts and recipes so teams can rebuild integrations without relying on legacy references.

## Execution Guidelines

- Track milestone progress in the project board; each milestone should close with a demoable artifact (tests, sample, or runtime feature).
- Embrace breaking changes—delete legacy code paths as soon as the replacement lands to keep scope focused.
- Keep proposals in sync with implementation; open an ADR when the runtime architecture stabilizes.

## Recommendations

- **Define milestone exit gates up front.** Capture explicit acceptance checks (runtime perf baselines, adapter coverage, docs parity) for every milestone so teams can self-certify readiness before moving forward.
- **Create a shared migration scorecard.** Publish a living matrix that maps samples, adapters, and tooling to their target milestone; update it weekly to surface blockers before they stall downstream work.
- **Staff an enablement pair for each cutover.** Assign one runtime engineer and one consumer-team owner to co-drive adoption tasks, ensuring feedback feeds back into the domain project without lag.
- **Bundle tests with guidance.** When landing new runtime capabilities, check in matching verification suites and concise "run this" instructions so consumers can validate locally without rediscovering the workflow.
- **Automate regression sweeps post-M3.** Trigger nightly pipelines that execute cross-adapter scenarios against the new runtime, catching integration drift early while legacy surfaces are phased out.

## References

- [Koan.Canon.Domain Technical Notes](../../src/Koan.Canon.Domain/TECHNICAL.md)
- [Canonization Overhaul Proposal](../proposals/PROP-canon-overhaul-2.md)
- [Architecture Principles](principles.md)
