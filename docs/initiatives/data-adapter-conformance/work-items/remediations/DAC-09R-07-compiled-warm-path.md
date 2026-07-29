---
type: SPEC
domain: data
title: "DAC-09R-07 Compiled bounded warm path"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: red
  scope: D E P compiled-path remediation
---

# DAC-09R-07 — Compile mapping and structural decisions once

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-06 |
| Unlocks | DAC-09 re-entry |
| Required primer profiles/IDs | D-05, D-06, D-07, E-10, P-02, P-03, P-04 |
| Production writes | Allowed only for shared compiled routing/mapping/materialization paths |
| Allowed paths | `src/Koan.Data.Abstractions/Records/**`; `src/Koan.Data.Core/Direct/**`; `src/Koan.Data.Core/Mapping/**`; `src/Koan.Data.Core/Patch/**`; bounded plan options/constants and focused mapping/Direct/record/patch tests; card evidence/ledgers |
| Forbidden paths | Connectors, source policy semantics, claims, diagnostics, unrelated work |
| One semantic owner | Framework compiled mapping/materialization plan |

## Meaningful outcome

After host composition and first bounded plan compilation, each data operation binds values and dispatches without
assembly scans, repeated reflection, unbounded materialization, or a second serializer/mapping model.

## User contract

- **Application expression:** ordinary Entity, Direct, patch, and `RecordSet.Project<T>()` calls.
- **Complete intent surface:** choose explicit Direct bounds when defaults are insufficient; no framework internals.
- **Guarantee:** finite host-owned plan caches, caller cancellation propagation, positive record/byte/value/duration
  limits, one mapping plan, and corrective projection failure for an incomplete required shape.
- **Correction:** an unbound entity/source, exceeded limit, or missing required property fails before unbounded work and
  names the public bound or mapping correction.
- **Public concepts:** existing mapping and limit concepts only.

## Execution

Compile entity/direct reflection decisions once, eliminate repeated DI enumeration, pass caller cancellation, replace
`long.MaxValue` limits, route patch through the mapping/value-conversion owner, and make DTO projection completeness
explicit.

## Verification

Structural counters and allocation/dispatch/elapsed/provider-work observations prove a quiet bounded warm path; focused
mapping, RecordSet, Direct, and patch behavior remains green.

