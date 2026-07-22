---
type: WORK
domain: framework
title: "R13-06 - First value-led 0.20 promotion slice"
status: passed
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: verified
  scope: seven promoted package owners and their direct evidence
---

# R13-06 — First value-led 0.20 promotion slice

## Outcome

Promote seven useful package owners as a cohesive first slice while improving the shared proof used by
later provider families:

- `Sylin.Koan.Testing`
- `Sylin.Koan.Testing.Containers`
- `Sylin.Koan.Testing.Hosting`
- `Sylin.Koan.Cache.Adapter.Sqlite`
- `Sylin.Koan.Data.SoftDelete`
- `Sylin.Koan.Web.Admin`
- `Sylin.Koan.Web.Auth.Connector.Test`

These packages are included because their application-facing promises are useful and independently
evidenced. Internal reverse-dependency count is not an inclusion criterion.

## Exploration and placement

**Application intent:** An application or provider author installs one of these packages, calls the
normal `AddKoan()` composition path, and receives the documented testing, cache, soft-delete,
diagnostics, or local-auth behavior without repository-only setup.

**Public expression:** No new application vocabulary is introduced. The proof exercises the same
package references, host composition, Entity/provider semantics, and protocol endpoints an application
uses. Maintainers use ordinary family tests, a clean package consumer, generated-surface checking, API
baselines, and one direct native-provider workflow.

**Guarantee:** A promoted owner has `0.20` version intent, belongs to an accepted supported claim, keeps
its public dependency closure supported, preserves its public API baseline when one exists, and passes
the evidence appropriate to its promise. Missing real infrastructure is not proof of provider support.

**Complete intent surface:** Product claims name the outcome and link documentation/evidence. Family
projects own behavior, lifecycle, and failure diagnostics. Package metadata owns version/API posture.
No parallel admission-cell model or terminal owner ledger participates in the application contract.

**Existing patterns reused:** `ProductSurfaceCompiler`, `PackageBaselineValidator`,
`GeneratedOutputVerifier`, `KoanIntegrationHost`, `Koan.Testing.Containers`, Cache/Web adapter test kits,
the bounded bootstrap runner, and direct `dotnet test` workflow steps.

**Coalescence:** Lifecycle and fail-closed corrections stay in the owning host, fixture, adapter suite,
or family script. Generic admission/result/native-candidate/reconciliation services were removed because
they duplicated ordinary test and workflow ownership without adding an application guarantee.

**Ergonomics:** Application code remains unchanged. A maintainer can trace claim → evidence path → test
project and run that project directly. Provider-native proof is visible as a normal workflow job rather
than derived from central per-cell metadata.

## Delivered evidence and corrections

- Testing helpers now preserve honest async lifecycle, ambient ownership, failed-start cleanup, and
  fixture diagnostics. A real PostgreSQL lifecycle spec exercises two complete owned lifecycles.
- Cache SQLite uses the reusable Cache adapter conformance contract and clears its owned SQLite pool on
  disposal so durable restart proof is honest.
- Web adapter fixtures share container lifecycle behavior instead of duplicating provider helpers.
- Auth Test and Web Admin integration hosts use deterministic logging and ephemeral test data protection.
- Soft Delete, Web Admin, Auth Test, Cache SQLite, and the three Testing packages retain their focused
  family evidence paths in `product/claims.json`.
- `Wave0PackageConsumerTests` packs the seven candidates and restores, compiles, and runs their public
  expressions outside the repository.
- Generated product truth, 0.20 dependency closure, package-validation baselines, and release checks
  remain executable repository guards.
- Adapter Forge runs direct `dotnet test`, requires the full 5-cell record or 4-cell vector contract,
  and rejects nonzero process exits, incomplete/duplicate/unexpected cells, and unknown outcomes.

## Validation boundary

Focused validation for this slice comprises:

1. `dotnet test tests/Koan.Packaging.Tests/Koan.Packaging.Tests.csproj -c Release`
2. the affected Testing, Cache, Soft Delete, Web Admin, and Auth Test projects;
3. `pwsh scripts/test-bootstrap.ps1 -Lane Fast -Configuration Release`;
4. `pwsh scripts/forge-verify.ps1 -DockerFree -Configuration Release`;
5. `dotnet run --project tools/Koan.Packaging -- product-surface --check`;
6. `dotnet run --project tools/Koan.Packaging -- api-baselines`;
7. the direct PostgreSQL provider proof for the sole `KoanLane=native` fact; and
8. the cheap repository-coherence check on the exact main PR commit.

## Exit state

The seven owners have supported claims and 0.20 intent with dependency closure. Their behavioral and
package-only evidence remains in the repository. The next R13 slice should select the next cohesive
provider family by user value and run the same proportional proof—without reopening a fixed owner wave
or adding central execution metadata.

Validation on 2026-07-21 passed:

- complete packaging and clean-consumer suite: 60/60;
- retained focused Testing and promoted-family facts: 52/52, including the real PostgreSQL lifecycle;
- bounded Fast bootstrap: 20/20;
- Docker-free Forge: five adapters green, 23/23 required cells present and passed;
- generated product surface: 33 claims and 93 packages, current;
- API posture: 35/42 configured baselines, seven first-publication cases, three content-only owners;
- complete Release solution build: zero warnings and zero errors.

The first simplified merge-ratchet run subsequently found and localized three evidence defects. The
host wrapper now disposes an incompletely started generic host without invoking an invalid stop
sequence, the Mongo Web bridge preserves the original authentication database while assigning its
isolated test database, and the package-only consumer packs the complete evaluated project-reference
closure rather than assuming newer dependencies already exist on nuget.org. Focused repair evidence
passes the host failure oracle, Communication 44/44, the affected Data correction 1/1, and real Mongo
Web behavior 52/52. Connected package-consumer and exact-ratchet runs passed as one-time evidence;
future merges do not inherit either as a universal publication prerequisite.
