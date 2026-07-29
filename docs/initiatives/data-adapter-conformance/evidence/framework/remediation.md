---
type: REFERENCE
domain: data
title: "Koan.Data Framework Remediation Ledger"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: finite DAC-01 findings routed to the DAC-02 contract gate
---
# Koan.Data framework remediation

| Remediation | Disposition | Owner | Invalidated consumers | Re-entry proof |
|---|---|---|---|---|
| REM-001 / FND-001 | Decide immutable source root, composition, and policy (`DEC-01`–`DEC-03`) | Framework | all source consumers | compile contract plus A/C/P cells |
| REM-002 / FND-002 | Separate readiness/provisioning and dispatch retry boundary (`DEC-05`, `DEC-14`) | Framework | every adapter | exact failure and no-replay cells |
| REM-003 / FND-003 | Replace dictionary/JSON neutral materialization (`DEC-07`, `DEC-08`) | Framework | Direct and Source Integration | D-05–D-08 plus allocation proof |
| REM-004 / FND-004 | Compile host-scoped provider-neutral mapping plans (`DEC-09`, `DEC-10`, `DEC-16`) | Framework | relational/document adapters | E/P mapping cells |
| REM-005 / FND-005 | Correct public transaction atomicity contract (`DEC-15`) | Framework | transaction consumers | G-05 fault proof |
| REM-006 / FND-006 | Make family handled/atomic receipts equal native work (`DEC-14`, `DEC-15`) | Family | KeyValue adapters | B-07/G-05/P-04 plan proof |
| REM-007 / FND-007 | Add source, inspection, result, mapping, and operation capabilities | Framework | all adapters | shared runner and Explain receipts |
| REM-008 / FND-008 | Apply one policy/effect gate to every alternate path (`DEC-03`, `DEC-04`) | Framework | all write/native paths | C-01–C-05 negative matrix |
| REM-009 / FND-009 | Add exact failure mapping and public redaction (`DEC-14`, `DEC-17`) | Framework + Adapter | diagnostics and claims | H-04–H-06 reconciliation |
| REM-010 / FND-010 | Materialize the ratified compact public language | Framework | all target examples | consumer compile contract |

These are shared-owner remediations. None may be closed by an SQLite-, MongoDB-, or other adapter-local shim.
