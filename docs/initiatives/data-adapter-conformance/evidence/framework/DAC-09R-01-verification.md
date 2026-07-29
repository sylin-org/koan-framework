---
type: EVIDENCE
domain: data
title: "DAC-09R-01 Foundation harness verification"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: DAC-09R-01 and DAC-09R-01A focused verification
---

# DAC-09R-01 verification

## Result

PASS. The shared G-09 managed field is declared inside the real host's `AddKoan(...)` composition and the oracle proves
read, keyed-read, conflict-aware write, and explicit-delete isolation without importing provider-bounded mass deletion
into the RowScoped claim. InMemory and JSON each pass all six record Forge rows.

The sealed consumer contract is a normal Data Core test-project compile item. Its first compile exposed that `.Sql(...)`
existed only in a private test extension; DAC-09R-01A replaced that fake with one immutable Relational Family
`SqlOperationBinding`. The binding is always opaque, so the Framework still requires a provider-enforced read lane.

The dynamic surface map now assigns the ratified V-01–V-24 catalog to the Vector Family surfaces. The scorecard projects
all 105 primer IDs. Workspace checkpoints require a non-empty all-green gate receipt and the mutation suite proves a RED
receipt cannot be sealed.

## Reproduced checks

| Check | Result |
|---|---|
| `Koan.Testing.Containers` build | PASS |
| AdapterSurface TestKit build | PASS |
| Data Core build including `consumer-contract.cs` | PASS |
| `SourceIntegrationSpec` | 20/20 PASS |
| InMemory `G-09/row/Adapter` | 1/1 PASS |
| JSON `G-09/row/Adapter` | 1/1 PASS |
| Forge record/InMemory | 6/6, GREEN |
| Forge record/JSON | 6/6, GREEN |
| Dynamic surface map | 52 surfaces, 3,147 declarations, 417 critic matches |
| Framework scorecard | 105/105 rows projected |
| Initiative protocol | 41 cards, 41 progress rows, 105 primer IDs, 22 packets |
| Mutation protocol | 16/16, including `checkpoint-rejects-red` |
| `git diff --check` | PASS; line-ending notices only |

Restore-free builds emitted NU1900 warnings because the sandbox could not reach NuGet vulnerability metadata; all
affected projects compiled with zero compiler errors. This environmental warning is not counted as a semantic PASS for
the later full clean certification gate.

