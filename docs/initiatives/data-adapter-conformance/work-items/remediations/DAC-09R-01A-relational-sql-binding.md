---
type: SPEC
domain: data
title: "DAC-09R-01A Relational SQL operation binding"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: compile-ready Relational Family operation binding
---

# DAC-09R-01A — Materialize the promised Relational `.Sql(...)` binding

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-01 compile probe |
| Unlocks | DAC-09R-01 completion |
| Required primer profiles/IDs | F-01, F-02, F-04, F-05, F-06 |
| Production writes | Relational Family binding only |
| Allowed paths | `src/Koan.Data.Relational/SourceIntegration/**`; `tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj`; `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/SourceIntegration/SourceIntegrationSpec.cs`; sealed consumer fixture and card evidence/ledgers |
| Forbidden paths | Connectors, Framework operation policy/execution, other families, unrelated work |
| One semantic owner | Relational Family native operation binding |

## Meaningful outcome

An application can give a registered read a compact SQL binding without using the hidden `.Native(...)` extension hook.

## User contract

- **Application expression:** `query => query.Lane("Reports").Sql("select ...")`.
- **Complete intent surface:** reference the Relational Family package, declare a provider-enforced read lane for opaque
  SQL, then declare parameters and bounds.
- **Guarantee:** `.Sql(...)` creates one immutable Relational binding carrying the exact command text and truthfully marks
  it opaque; it never infers Read from a text prefix.
- **Correction:** blank SQL rejects at composition; an opaque binding without a validated read lane rejects before
  provider work through the Framework effect gate.
- **Public concepts:** one discoverable `Sql` verb and one immutable binding payload are required for Relational lowering.

## Execution and verification

Replace the private fake extension with the production Relational binding, compile the sealed consumer contract through
an explicit Relational project reference, and prove blank text plus opaque effect behavior in focused tests.

