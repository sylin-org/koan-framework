---
type: SPEC
domain: data
title: "DAC-09R-01 Foundation certification harness"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: DAC-09 Foundation remediation
---

# DAC-09R-01 — Make the Foundation gate executable and reproducible

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09 RED receipts |
| Unlocks | DAC-09R-02 |
| Required primer profiles/IDs | G-09 plus the complete certification protocol |
| Production writes | Test infrastructure only; no adapter or runtime semantics |
| Allowed paths | `src/Koan.Testing.Containers/KoanDataSpec.cs`; `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs`; `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/ManagedFieldNoLeak.cs`; `tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj`; `docs/initiatives/data-adapter-conformance/evidence/framework/consumer-contract.cs`; `docs/initiatives/data-adapter-conformance/tools/New-FrameworkSurfaceMap.ps1`; `docs/initiatives/data-adapter-conformance/tools/New-FrameworkScorecard.ps1`; `docs/initiatives/data-adapter-conformance/tools/New-InitiativeCheckpoint.ps1`; `docs/initiatives/data-adapter-conformance/tools/Test-Initiative.Mutations.ps1`; focused fixtures/tests/evidence and initiative ledgers |
| Forbidden paths | Runtime Data semantics, connector production, public product truth, unrelated work |
| One semantic owner | Framework conformance and certification harness |

## Meaningful outcome

Maintainers can run one strict gate that reaches real adapter behavior, compiles the promised application surface, maps
all primer cells, and seals the exact dirty-workspace candidate it judged.

## User contract

- **Application expression:** `services.AddKoan(() => ManagedFieldNoLeak.Declare());` in the shared host fixture, while the
  sealed `consumer-contract.cs` remains ordinary compiling C#.
- **Complete intent surface:** run strict Forge, build the Data Core contract project, regenerate the surface scorecard,
  and mint a checkpoint only after every required lane is green.
- **Guarantee:** G-09 registration occurs during real composition; a namespace/API regression, unmapped primer cell, or
  candidate-identity mismatch is observable.
- **Correction:** fail the gate with the exact missing declaration, cell, compile error, or identity mismatch; never
  weaken the composition guard or award a checkpoint to RED.
- **Public concepts:** none; this card tests the already-ratified public language.

## Evidence to read

- `evidence/framework/DAC-09-certifier.md`
- `evidence/framework/DAC-09-adversarial-review.md`
- the exact allowed source/tool paths above

## Execution

1. Reproduce the post-composition G-09 failure and the consumer-contract compiler error.
2. Add a composition-declaration hook to the shared test host without changing its post-`AddKoan()` DI hook.
3. Separate managed-field declaration from oracle execution and invoke the declaration inside `AddKoan(...)`.
4. Make `consumer-contract.cs` compile as part of an ordinary restore-free project build.
5. Correct V-cell scorecard projection without special-casing V-01 and prove all 105 primer IDs map.
6. Seal a complete source/test/tool workspace manifest only after the rerun is green.

## Verification

- Focused InMemory and JSON G-09 rows pass and reach persistence behavior.
- Data Core contract project builds with `--no-restore`.
- Framework scorecard contains all 105 IDs exactly once.
- Initiative mutation tests reject a forged green checkpoint.

## Stop conditions

Any correction that weakens `ManagedFieldRegistry` composition ownership, special-cases one provider/cell, or mints a
checkpoint before green stops this card.
