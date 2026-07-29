---
type: SPEC
domain: data
title: "DAC-09R-06 Diagnostics and privacy boundary"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: H and P diagnostics/privacy remediation
---

# DAC-09R-06 — Make every public explanation inert and redacted

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-05 |
| Unlocks | DAC-09R-07 |
| Required primer profiles/IDs | H-01, H-02, H-05, H-06, P-01 |
| Production writes | Allowed only for public diagnostics, failure, and transaction evidence boundaries |
| Allowed paths | `src/Koan.Data.Abstractions/Sources/DataSourcePlan.cs`; `src/Koan.Data.Abstractions/Diagnostics/**`; `src/Koan.Data.Core/Axes/DataAxis.cs`; `src/Koan.Data.Core/DataService.cs`; `src/Koan.Data.Core/Diagnostics/**`; `src/Koan.Data.Core/Transactions/**`; focused diagnostics/privacy/transaction tests; card evidence/ledgers |
| Forbidden paths | Connectors, routing semantics, transfer, mapping, unrelated work |
| One semantic owner | Framework public diagnostic and restricted-evidence boundary |

## Meaningful outcome

An application can explain a source or failed operation without opening provider resources or exposing secrets,
business identifiers, native prose, or native exception objects.

## User contract

- **Application expression:** `Data.Source("LegacyErp").Describe()` / `.Explain(operation)`; explicit `.Doctor(ct)` for
  active checks.
- **Complete intent surface:** none beyond choosing the source/operation; Doctor remains the only activating action.
- **Guarantee:** all Describe/Explain variants project the same frozen plan; public settings are allowlisted/redacted;
  public failures/logs use stable codes and corrections while exact native evidence stays bounded and restricted.
- **Correction:** unavailable detail is represented by a stable reference, never raw provider text.
- **Public concepts:** no new surface; consolidate alternate Explain paths behind the canonical service.

## Execution

Delete the adapter-creating diagnostics bypass, redact plan settings by allowlist, remove identifiers/native exceptions
from transaction logs and public exceptions, and route native detail to the restricted evidence store.

## Verification

Warm Describe/Explain performs zero activations; seeded secret/business/native markers are absent from public output,
logs, exception graphs, facts, and health.

