# TEST-0001 Koan testing platform realignment

**Status:** Accepted 2025-10-06  \
**Driver:** QA & DX working group  \
**Deciders:** Koan Core maintainers, QA council  \
**Impacted:** All Koan test contributors

## Contract

- **Inputs:** Existing heterogeneous `tests.old` tree, requirements for parallelism, deterministic fixtures, structured diagnostics.
- **Outputs:** New `tests/` hierarchy with shared harness (`Shared/Koan.Testing`), suite manifests, deterministic seed packs, and standardized project layout.
- **Error modes:** Fixture allocation failure, stale seed packs, missing suite manifest, non-isolated resource usage.
- **Success criteria:** Any suite scaffolded under `tests/` runs in parallel without cross-talk, yields structured JSON diagnostics, and consumes shared pipelines/fixtures without local reinvention.

## Context

Legacy tests accreted over time with inconsistent project naming, ad-hoc fixtures, and varying frameworks. Parallel test execution was brittle and diagnostics noisy. The Koan framework now prioritizes a greenfield testing approach focused on clarity, speed, and reusability.

## Decision

1. **Archive legacy surface:** Move the prior `tests` root to `tests.old` for reference only.
2. **Establish opinionated structure:** Create a fresh `tests/` tree with three pillars:
   - `Shared/Koan.Testing/` – reusable harness, pipeline abstractions, diagnostics, fixture registry.
   - `SeedPacks/` – deterministic data packs referenced by fixtures.
   - `Suites/<Domain>/<Scope>/<Project>/` – canonical suite layout aligned with runtime modules.
3. **Introduce pipeline facade:** All future specs run through a `TestPipeline` abstraction that orchestrates Arrange/Act/Assert, fixture lifetimes, and diagnostics.
4. **Standardize diagnostics:** Implement structured JSON logging (`TestDiagnostics`) with suite/spec metadata to support parallel-friendly outputs and CI ingestion.
5. **Codify fixtures:** Ship a registry-driven fixture model (`FixtureRegistry`, `SeedPackFixture`) guaranteeing isolation and deterministic setup/teardown.
6. **Centralize infrastructure helpers:** House external-environment utilities (e.g., Docker probing, container bootstrap defaults) under `Shared/Koan.Testing/Infrastructure` with corresponding fixtures (`DockerDaemonFixture`) so integration suites can detect capabilities without bespoke code.
7. **Document and enforce:** Add `tests/README.md`, `SeedPacks/README.md`, and guidelines for suite scaffolding. Future suites must reference `Shared/Koan.Testing` and follow the naming contract `Koan.Tests.<Domain>.<Scope>`.
8. **Govern via ADR:** Capture this policy here; future deviations require amendments to `TEST-0001` or additional ADRs.

## Rationale

- **KISS:** Consolidated structure reduces mental overhead; contributors know exactly where shared pieces live.
- **DRY:** Shared harness prevents per-suite reinvention of fixtures, logging, and utility helpers.
- **SoC:** Domain suites stay focused on business logic while `Shared/` handles infrastructure concerns.
- **Parallel-first:** Fixture registry and deterministic seed packs eliminate shared state collisions, unlocking full parallel execution.
- **Observability:** Structured diagnostics improve CI triage and make flake detection feasible.

## Edge cases

1. **Heavy integration fixtures** (e.g., multi-container topology) – handled via composed fixtures registered in `FixtureRegistry` with explicit lifecycle hooks.
2. **Legacy seeds requiring bespoke formats** – convert to JSON seed packs or wrap via fixture adapters documented in-suite.
3. **Specs needing serialized execution** – suite manifest may opt out with documented justification, but must remain isolated.
4. **Cross-platform nuances** (Windows vs. Linux containers) – fixtures expose capability flags; pipeline selects compatible providers.
5. **Flaky external dependencies** – fixtures support deterministic fallbacks (in-memory fakes) and emit warnings when remote services degrade.

## Consequences

- Existing projects must be migrated into the new layout; legacy helpers duplicated per suite will be deleted once replaced.
- CI configuration will shift to shard execution across the new suites and capture JSONL diagnostics artifacts.
- Tooling work (CLI scaffolder, lint rules) follows this ADR and may be enforced via subsequent decisions.

## Follow-up actions

- Build out the `Shared/Koan.Testing` harness (pipeline, diagnostics, fixture registry) and reference it from the first suite (`Koan.Tests.Core.Unit`).
- Establish shared container fixtures (Docker probe, base Testcontainers defaults) to unblock integration migrations (Docker probe + daemon fixture landed 2025-10-06; Redis/Postgres container fixtures migrated the same day; Mongo container fixture added alongside driver infrastructure; additional adapters pending).
- Port representative legacy specs into the new structure, starting with JSON utility tests.
- Update CI workflows to target the new solution slice once suites are migrated.
- Draft analyzer rules to ensure future suites comply with naming and reference requirements.
