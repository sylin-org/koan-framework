---
type: REFERENCE
domain: canon
title: "Trusted records"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/records.md - route table verified against leaf targets
---

# Trusted records

Canon exists for one shape of problem: repeated, conflicting descriptions of the same real
thing, where the surviving Entity must stay explainable - arrivals preserved, matches
deterministic, field provenance retained, ambiguity routed to a human.

## Route by need

| The request says | Fetch |
|---|---|
| "three systems send us the same customer, differently" | [Canon reconciliation](records/canon.md) |
| "merge duplicate listings and keep the source of every field" | [Canon reconciliation](records/canon.md) |

## Standing constraints

- Arrivals are preserved, never silently overwritten; the canonical Entity's provenance names
  where each field came from.
- Commit is explicit - reconciliation proposes, a human or policy disposes.

## Do not, at this level

- Do not reach for Canon when the input is merely invalid - ordinary validation is the smaller
  capability.
- Do not auto-commit reconciliations without a stated policy.

For the one-screen maturity view, see
[Trusted records in the capability map](../reference/capability-map.md#trusted-records).
