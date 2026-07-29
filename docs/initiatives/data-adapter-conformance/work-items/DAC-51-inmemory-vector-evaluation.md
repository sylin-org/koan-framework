---
type: SPEC
domain: data
title: "DAC-51 Evaluate and Certify the InMemory Vector Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: InMemory Vector adapter evaluation prompt
---

# DAC-51 — Evaluate and certify the InMemory Vector adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-30 |
| Primer scope | dynamically selected Source Core and ratified Vector manifest |
| Production writes | forbidden |
| Owner | Adapter(InMemory Vector) |

## Meaningful outcome

The infrastructure-free adapter becomes a fast semantic oracle for vector operations without claiming persistence,
durability, native indexing, or external-provider behavior it cannot supply.

## Execute

1. Freeze its claim manifest and create `evidence/inmemory-vector/`; inspect all source, tests, facts, and docs afresh.
2. Prove value/vector round trips, upsert replacement, fetch/delete outcomes, empty and duplicate cases, partitions and
   source axes, dimensions, supported distance metrics, deterministic `topK`, tie ordering, and filter semantics.
3. Prove source isolation across hosts/tests, cancellation, concurrency, disposal, bounded memory behavior, and warm
   allocation/latency baselines appropriate to an in-process implementation.
4. Require explicit corrective declines for durability, restart persistence, provider-native inspection, and any
   algorithm/index guarantee not actually implemented.
5. Run strict Forge. Any RED creates one-owner remediation cards; do not edit production code in this evaluation.

## Verification

- Complete shared Source Core/Vector cells, oracle comparisons, concurrency/isolation tests, and packet validation.
- A mutation to ordering, partition isolation, or a declared capability is observed red.

## Definition of done

- [ ] The adapter is green for a narrow, truthful in-process manifest.
- [ ] It is designated a semantic test oracle, not a real-provider gold reference.
- [ ] Every non-applicable durability/native-provider row has an explicit decline.

## Stop conditions

Hidden process-global state, nondeterministic ties, an overstated persistence claim, or production edits block PASS.
