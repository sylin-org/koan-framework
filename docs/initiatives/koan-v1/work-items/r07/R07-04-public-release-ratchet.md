---
type: SPEC
domain: framework
title: "R07-04 - Restore a Trustworthy Public-Release Ratchet"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: in-progress
  scope: whole-repository test truth required before the first automatic public release
---

# R07-04 — Restore a trustworthy public-release ratchet

- Tranche: `T6 — semantic capability ring`
- Status: `in-progress`
- Depends on: R07-03
- Unlocks: canonical Lifecycle; advances the first trusted automatic `dev` publication, which remains
  gated by [PMC-016 and PMC-017](../../POST-CYCLE-TODO.md#current-register)
- Owner: repository test architecture and the public-release ratchet

## Meaningful outcome

The exact command used by the protected `dev` release workflow is green from a clean checkout, or a
real product/test defect keeps it red with one actionable owner. No helper library is launched as a
test assembly, no suite relies on leaked process-static host state, and no infrastructure lane is
silently skipped merely to permit publication.

```powershell
pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease
```

This is not a request for a blanket “make CI green” cleanup. It restores one meaningful guarantee:
Koan's automatic publisher cannot pass a repository state whose declared release floor is red.

## Evidence at opening

R07-03's affected surface is green: packaging passes 52/52; the public-release solution build,
composition lock, docs, changed examples, skills, and blueprint legs pass. The whole-solution test leg
fails closed, and no package, tag, release, or remote ref is changed.

Isolated reruns show that the red result is not only whole-solution contention:

- Identity: 70/114 pass; 44 cases reject because no active host supplies `IDataService`.
- Canon integration: 4/6 pass; two Entity-backed flows reject through the same host-ownership seam.
- Jobs SQLite: 73/78 pass; five scheduling/concurrency behaviors fail independently.
- Mongo: 67/68 pass; the existing ZenGarden endpoint-precedence defect remains
  [PMC-012](../../POST-CYCLE-TODO.md#current-register).
- `Koan.Jobs.TestKit` is a shared helper library but is launched as a test assembly, aborting before
  discovery on its `AwesomeAssertions` runtime dependency. An existing Web test-kit pattern already
  marks this library shape `IsTestProject=false`.
- The concurrent solution run also reports provider-specific Jobs failures. Reproduce those after the
  deterministic local failures are removed; do not infer their root cause from the aggregate log.

## Progress

### Shared TestKit classification — passed

- Evaluated solution state identified one actual misclassification: `Koan.Jobs.TestKit` inherited
  `IsTestProject=true` from xUnit v2 even though it contains only shared abstract specs and fixtures.
  Data and Vector TestKits were non-runnable only because the property happened to remain unset.
- All five `*.TestKit.csproj` libraries now explicitly declare `IsTestProject=false`. The six concrete
  Jobs consumers and the Data, Vector, Web, and MCP consumer matrices continue to own execution.
- A release-workflow contract requires every TestKit under `tests/` to retain exactly that explicit
  boundary. The contract class passes 2/2, all five projects evaluate false, and the previously
  aborting direct Jobs TestKit invocation exits successfully without launching a test host.

### Deterministic packaging subprocess lifetime — passed

- Complete 53-test packaging reruns initially produced no result before 240- and 300-second outer
  timeouts; the known child trees were cleaned. Focused lineage, graph, and workflow contracts still
  passed 30/30.
- A bounded blame run located the wait in the Golden Journey probe's `dotnet build`: reusable MSBuild
  workers retained redirected pipe handles after the parent exited. An external node-reuse override
  first proved the diagnosis with Golden Journey 2/2 and aggregate packaging 53/53.
- `ProcessRunner` now disables MSBuild worker-node reuse for every packaging subprocess. This one
  child-scoped policy covers source probes and release-time restore, evaluation, build, and pack
  commands without an operator environment setting. The executable contracts pass 3/3 and the
  complete packaging suite passes 53/53 in 1 minute 20 seconds.

### Identity host selection and Canon persistence — passed

- Identity's 44 failures were one order-dependent test ownership cascade. Its shared fixture started a
  correct `AddKoan()` host; the intentional failed-start fact then replaced and correctly released the
  process default. Core deliberately did not resurrect the older fixture provider.
- One test-local base now selects `fixture.Services` through `AppHost.PushScope` for every complete
  xUnit fact flow. The negative host test selects its nested provider explicitly. No production host,
  lease, Entity, or Data fallback changed; Identity passes 114/114.
- Canon's 2 failures had a different root. `ICanonPersistence` owned canonical writes and indexes, but
  aggregation and rebuild bypassed it through `CanonEntity<T>.Get`; a legacy exception-message filter
  silently treated the former host error as an absent snapshot.
- `ICanonPersistence.GetCanonicalAsync<T>` now completes the storage boundary. Aggregation and rebuild
  load only through the configured implementation, default persistence alone lowers to Entity/Data,
  provider failures propagate, and explicit-service-provider extensions select that provider for the
  full operation. The obsolete message parser is deleted.
- The public interface addition intentionally advances `Sylin.Koan.Canon.Domain` from compatibility
  tier 0.17 to 0.18; automatic lineage owns its reverse-dependent rebuild. Package companions and the
  current Canon reference now state the exact default/custom persistence and host boundary.
- Canon unit passes 35/35 and integration passes 6/6. Focused proofs cover prior-state loading from a
  hostless custom store, rebuild through that store, failure propagation, and provider-scope
  restoration.

## Decisions

### DECIDED

- Keep the complete public-release ratchet. Do not exclude a failing runnable suite, soften failures,
  or publish from a narrower package-only lane.
- Classify shared test libraries as non-runnable at the project boundary; retain their compilation
  through their real consumer suites.
- Repair host ownership at its shared test/application seam. Do not reintroduce a process-static
  production fallback to satisfy tests.
- Treat Identity and Canon as distinct roots despite their shared exception: Identity owns explicit
  test-flow selection; Canon owns one complete replaceable persistence boundary.
- Treat Jobs behavior failures and Mongo endpoint election as their own behavioral roots, with focused
  evidence before the aggregate rerun.
- Lifecycle production changes remain stopped until this base is green. Stable release truth is a
  prerequisite, not post-cycle polish.

### DEFAULT

- Prefer one root repair that restores multiple suites over per-test initialization patches.
- Preserve explicit infrastructure lanes and their existing capability/availability reporting.

### OPEN

- Provider Jobs failures may disappear once local test isolation is restored; only isolated reruns may
  promote them into separate work.

## Red/green plan

1. **Complete.** Inventory every solution project that `dotnet test Koan.sln` treats as runnable;
   classify real suites, shared test libraries, and explicit infrastructure runners from evaluated
   project state.
2. **Complete.** Correct shared-library classification using the existing `IsTestProject=false`
   pattern and add a structural regression so another test kit cannot silently enter the release run.
3. **Complete.** Repair Identity's explicit test-flow selection and Canon's complete persistence
   boundary; rerun both complete projects and their focused regressions.
4. **Complete.** Make packaging subprocess lifetime deterministic and pass the complete 53-test suite
   without an operator environment override.
5. Reproduce the five Jobs SQLite failures from a clean isolated output, group them by root, and repair
   behavior or isolation without lowering assertions.
6. Resolve the explicit endpoint-precedence decision in
   [PMC-012](../../POST-CYCLE-TODO.md#current-register) and rerun Mongo 68/68.
7. Rerun any remaining provider Jobs failures individually, then run the exact public-release ratchet
   from a clean checkout.
8. Record exact counts, duration, environment-dependent skips, warnings, and the absence of publication
   or remote mutation before passing this child.

## Acceptance

- `dotnet test Koan.sln -c Release --no-build --nologo` does not launch shared helper libraries.
- Identity, Canon integration, Jobs SQLite, and Mongo each pass their complete isolated suite.
- Infrastructure-dependent suites either execute successfully or report an intentional existing lane
  decision; absence never becomes an accidental pass.
- The exact `pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease` command passes every
  leg from a clean checkout without retrying away a deterministic failure.
- No test exclusion, warning suppression, process-static production fallback, package publication, or
  remote Git mutation is introduced to obtain green.
