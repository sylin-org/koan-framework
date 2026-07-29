---
type: SPEC
domain: data
title: "DAC-09R-03 Operation effect chokepoint"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: C and F Framework remediation
---

# DAC-09R-03 — Require one explicit operation effect before dispatch

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-02 |
| Unlocks | DAC-09R-04 |
| Required primer profiles/IDs | C-01, C-04, C-05, F-05, F-06, F-11, H-06 |
| Production writes | Allowed only for operation planning and Direct/instruction entry points |
| Allowed paths | `src/Koan.Data.Abstractions/Instructions/**`; `src/Koan.Data.Abstractions/SourceIntegration/Operations/OperationEffect.cs`; `src/Koan.Data.Abstractions/SourceIntegration/Operations/OperationPlan.cs`; `src/Koan.Data.Abstractions/Diagnostics/DataOperationDescription.cs`; `src/Koan.Data.Core/Direct/**`; `src/Koan.Data.Core/Data.cs`; `src/Koan.Data.Core/DataService.cs`; `src/Koan.Data.Core/DataServiceExecuteExtensions.cs`; `src/Koan.Data.Core/SourceIntegration/Composition/**`; `src/Koan.Data.Core/SourceIntegration/Runtime/RegisteredOperationExecutor.cs`; `src/Koan.Data.Relational/Extensions/DataServiceExecuteExtensions.cs`; focused Direct/source-policy/operation tests; card evidence/ledgers |
| Forbidden paths | Connectors, source registry lifetime, transfer fallback, diagnostics, unrelated work |
| One semantic owner | Framework operation/effect plan |

## Meaningful outcome

An application states an operation's effect once; result shape can never grant read authority to opaque native work.

## User contract

- **Application expression:** registered operations use `.Query(...)`, `.Scalar<T>(...)`, or an explicit non-read
  instruction/effect; Direct opaque work supplies an explicit effect or a provider-enforced read lane.
- **Complete intent surface:** choose effect, result, delivery, lane, parameters, timeout, and bounds where applicable.
- **Guarantee:** the compiled plan gates policy before route/resource/callback/provider work; no SQL/text-prefix inference.
- **Correction:** absent or unverifiable effect becomes `Unknown` and fails closed with the safe registration/lane fix.
- **Public concepts:** existing `DataOperationEffect` is the only additional decision; result APIs do not imply it.

## Execution

Delete ambiguous string inference, route all Direct/transaction/instruction calls through the same plan gate, preserve
caller cancellation, and add adversarial mutating-scalar/multi-statement/side-effecting-function tests.

## Verification

Every alternate path rejects an opaque mutation under a read ceiling before route resolution or provider construction.

The exploration pass found three additional seams inside the same semantic owner: registered plans exposed a second
`OperationEffect` enum, `Data<TEntity>.Execute<TResult>(string)` inferred native result/effect from `TResult`, and the
ordinary `DataService` constructed an Entity repository before its instruction effect was gated. The allowed-path
inventory includes those exact contracts so R03 can remove, rather than bridge, the duplicate authorities and gate
normal-host provider construction.
