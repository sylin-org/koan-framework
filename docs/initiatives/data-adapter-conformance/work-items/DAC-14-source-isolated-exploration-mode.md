---
type: SPEC
domain: data
title: "DAC-14 Retire Source-Quarantine Scaffolding"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: lean greenfield-authoring boundary
---

# DAC-14 — Retire source-quarantine scaffolding

| Field | Value |
|---|---|
| Phase / kind | foundation / architecture correction |
| Depends on | DAC-02 |
| Unlocks | DAC-03 |
| Production writes | repository explore skill and initiative truth only |
| Owner | Data conformance workflow |

## Outcome

The proposed role registry, history-free exports, per-read access log, and three exploration modes are retired. They
cannot prove cognitive isolation and do not improve a shipped contract, adapter implementation, or hot path.

SQLite and MongoDB remain ground-up replacements. Their existing implementations may supply provider facts, public
compatibility decisions, negative lessons, and black-box cases; they do not supply the new type graph, control flow,
helpers, tests, or compatibility branches. The ordinary `explore` workflow already requires explicit `keep`, `absorb`,
`rebuild`, or `delete` decisions and treats current code as evidence rather than authority.

## Lean boundary

- Framework construction starts from the ratified public contract and shared ownership laws.
- Gold construction starts from an empty adapter implementation and the certified Framework/Family contracts.
- Every runtime type, cache, resource owner, dispatch boundary, and abstraction needs one contract or measured hot-path
  reason. Unexplained indirection is removed.
- Retirement is atomic: no old/new bridge, shadow registration, `Legacy`/`V2` path, or fallback survives.
- Black-box conformance and native-provider evidence prove behavior; provenance ceremony does not substitute for tests.

## Definition of done

- [x] Restore the concise default exploration skill.
- [x] Remove the quarantine reference and guard script.
- [x] Freeze the minimum-meaningful-parts rule in the public contract.
- [x] Preserve the ground-up replacement and atomic-retirement requirements.
- [x] Unblock DAC-03 without changing production behavior.
