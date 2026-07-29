---
type: SPEC
domain: data
title: "DAC-03 Build the Executable Conformance Control Plane"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Forge, TestKit, claim projection, and strict-runner implementation prompt
---

# DAC-03 — Build the executable conformance control plane

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-14 |
| Unlocks | DAC-49 |
| Primer scope | §§8–10; all stable IDs and evidence kinds |
| Production writes | only `src/Koan.Data.Abstractions/**` capability declarations, `src/Koan.Testing/**`, `tests/Suites/Testing/Koan.Testing.Tests/**`, `tests/Suites/Data/AdapterSurface/**`, generic infrastructure in `tests/Suites/Data/VectorAdapterSurface/**`, `scripts/forge-verify.ps1`, and initiative evidence/docs; no adapter semantics |
| Owner | Framework conformance |

## Meaningful outcome

An adapter claim mechanically selects its primer cells, runs the correct evidence, and produces a reproducible packet.

## Exploration decision

The required pre-implementation exploration is recorded in
[DAC-03-explore.md](../evidence/framework/DAC-03-explore.md). `DataCaps` remains runtime capability truth;
`Koan.Testing` owns the generated primer projection, claim manifest, packet/verdict rules, and executable 81-case base;
Forge owns process orchestration and strict exit classification. The existing AODB gate remains a bounded behavioral
consumer until the Vector annex can converge both planes without changing current semantics.

## Required work

1. Use the production-code `explore` workflow. Read `DataCaps`, adapter factories, runtime facts, AdapterSurface,
   VectorAdapterSurface, `EntityConformanceSpecs`, `scripts/forge-verify.ps1`, and product claim generation.
2. Evolve Forge/TestKit as the sole executable projection of the primer. Preserve the stable A–H/P IDs; do not create
   renamed Forge obligations.
3. Establish one executable claim declaration that can project:
   - runtime capabilities/facts;
   - Observed/Target/Declined applicability;
   - TestKit modules and negative paths;
   - evidence packet claims/scorecard skeleton; and
   - generated documentation/product-claim inputs.
   Reuse current capability declarations where possible; do not add a parallel manual JSON truth.
4. Add a registry integrity rule: every announceable capability maps to objective cells, and every selected cell maps
   to executable tests/evidence capture.
5. Add strict certification mode. Missing Docker/provider, skipped LIVE, missing expected cells, duplicate cells,
   unresolved evidence, and a false claim all fail/inconclusive with distinct exit codes; none returns green.
6. Provide packet generation/validation with safe artifact references and mechanical verdict aggregation.
7. Record each verdict's consumed semantic owners, source path/hash set, TestKit/Forge/schema identity, profile, and
   provider fixture. Add an impact query that invalidates all matching packets regardless of DAG direction.
8. Version the claim/evidence schema and embed the exact primer/profile fingerprint in every packet. Reject stale or
   incompatible packets explicitly so third-party adapters can target a known conformance protocol.
9. Seed modules around ratified framework contracts. Initially RED target cells are acceptable; absent registry/test
   mappings are not.

## Verification

- Mutation checks: add an unknown ID, orphan capability, skipped LIVE cell, duplicate cell, unresolved EV ref, stale
  profile fingerprint, and false advertised claim; each must fail for the correct reason.
- Impact mutations change a shared owner/path and Forge/schema version; every consuming upstream/sibling/downstream
  packet becomes stale while unrelated packets remain valid.
- Existing AODB cells remain runnable and map explicitly to primer IDs.
- Deterministic claim/scorecard generation produces byte-stable output for a fixed identity.
- Focused TestKit/tool builds and tests pass; full solution builds before handoff.

## Definition of done

- [x] All 81 current base IDs are registered with applicability and required evidence; annex projection is extensible
  without another semantic catalog.
- [x] Forge strict mode and packet validation distinguish RED, DEFER/inconclusive, and infrastructure failure.
- [x] Impact invalidation is dependency-based and cannot preserve a packet that consumed changed code/tooling.
- [x] No second semantic catalog or manually synchronized claim table was introduced.
- [x] DAC-04 can land framework behavior red-first against this control plane.

Verification: [DAC-03-verification.md](../evidence/framework/DAC-03-verification.md).

## Stop conditions

Stop if the one-claim-truth design requires a new public runtime type not ratified by DAC-02, or if existing Forge
behavior cannot be preserved without redefining primer semantics.

## Explore record

**Task**

Replace the inferred AODB gate with the smallest executable projection of the primer.

**Application intent**

An adapter author inherits the shared record or vector conformance suite. Each inherited test reports the exact
`<Acceptance ID>/<Case>/<Owner>` it proves, and Forge returns a machine-readable verdict without a second semantic
catalog.

**Public expression**

```csharp
[Fact(DisplayName = "G-09/row/Adapter: row scope is adversarially isolated")]
public Task Row_scope_is_isolated() => ...;
```

```powershell
pwsh scripts/forge-verify.ps1 -Adapter Sqlite -Plane record -Strict
```

**Guarantee/correction**

The primer owns IDs and evidence kinds. TestKit owns executable proof. Forge checks exact row keys, missing and
duplicate cells, skips, failures, and infrastructure errors. It never infers obligations from a filename or method
name.

**Complete intent surface**

The initial executable bindings cover the existing record and vector isolation, bounded-streaming, and polymorphic
round-trip proofs. Unbound primer requirements remain explicit RED work for DAC-04 through DAC-08; placeholder tests
are forbidden.

**Public concepts**

No new runtime concept. The existing `CapabilitySet`, `DataCaps`, xUnit `Fact`, shared TestKit bases, and the primer row
key are sufficient.

**Docs read**

`docs/architecture/principles.md`, `docs/architecture/product-constitution.md`,
`docs/architecture/data-adapter-development-primer.md`, `docs/decisions/DATA-0110-compact-data-adapter-language.md`,
`docs/architecture/adapter-forge.md`, `docs/architecture/adapter-forge-rfc.md`, `src/Koan.Testing/README.md`, and
`src/Koan.Testing/TECHNICAL.md`.

**Code read**

`DataCaps`, `CapabilitySet`, `IDataAdapterFactory`, `DataProviderCatalog`, runtime-fact recording,
`EntityConformanceSpecs`, both AODB TestKit bases, the link-compiled `CapabilityConformanceGate`, its tests, both
TestKit projects, `scripts/forge-verify.ps1`, and product-claim lint inputs.

**Reusing**

`CapabilitySet` remains the runtime truth; the existing behavioral cells remain useful proofs; TRX remains the test
transport; the primer remains the catalog.

**Creating new**

| Moving part | Owner | Why it exists |
|---|---|---|
| one capability gate source in `tests/Suites/_shared` | AODB TestKits | preserves one fail-closed dispatcher without imposing a transitive runtime package edge on adapter suites |
| exact row-key parsing in Forge | Framework conformance | binds results to primer semantics and detects structural gaps |

**Coalescence**

Keep the small gate link-compiled into both test planes and delete its unnecessary public action enum. `Koan.Testing`
owns the semantic catalog, manifests, packets, and verdict rules; the gate remains test-host dispatch only. Do not add a
runtime claim hierarchy, generated capability mirror, manual JSON claim table, or placeholder test catalog.

**Ergonomics**

Adapter suites stay inheritance-only. Framework test authors add one readable row-key prefix to a normal `Fact`.
Operators run one command and receive distinct RED, INCONCLUSIVE, infrastructure-error, and GREEN outcomes.

**Constraints satisfied**

No adapter semantics, package versions, NuGet configuration, or production hot path changes. The design uses existing
dependencies and keeps stable A-H/P IDs.

**Risks**

TRX adapters may vary in whether the display name appears as `testName`; Forge must reject an unparseable result rather
than guess. Existing unbound primer rows must remain visible as planned work without turning the current narrow suite
permanently red by pretending placeholder coverage exists.
