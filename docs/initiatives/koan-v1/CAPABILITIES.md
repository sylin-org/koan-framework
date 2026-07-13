---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Capability Evidence Ledger"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: evidence vocabulary and initial assessment queue
---

# Koan V1 Capability Evidence Ledger

This ledger controls what the initiative may claim about Koan. A capability is not supported merely
because an API, sample, or document exists. Record the implementation, executable evidence,
documented contract, known limits, and ownership before promoting a claim.

[`PROGRESS.md`](PROGRESS.md) tracks work. This file tracks product truth. R02 owns the first complete
assessment; until then, every surface below is deliberately marked `unassessed`.

## Maturity vocabulary

Use one of these labels for every assessed capability:

| Label | Meaning |
|---|---|
| `specified` | The intended contract is written, but executable evidence is absent or incomplete. |
| `demonstrated` | A maintained sample or focused exercise proves a useful path; compatibility is not yet promised. |
| `experimental` | Demonstrated or selectively verified, but intentionally outside compatibility guarantees. |
| `verified` | Automated tests cover the stated contract and its important failure modes. |
| `supported-extension` | Verified, documented, packaged, and within an explicit compatibility boundary. |
| `supported-foundation` | A stable, verified default that other supported Koan capabilities may rely on. |
| `deprecated` | Still present for migration, with a named replacement and removal policy. |
| `retired` | No longer part of the supported product surface. |

`Unassessed` is an assessment state, not a maturity label. Do not translate it into a public claim.

## Evidence requirements

An assessed entry must identify:

1. the user outcome and shortest supported path;
2. the public entry point and owning package;
3. current code locations;
4. automated tests and what they actually prove;
5. maintained samples or executable documentation;
6. startup, error, and inspection behavior;
7. supported and unsupported scenarios;
8. the compatibility or removal expectation;
9. the date and commit assessed.

Private downstream use may reveal questions, but it is neither citable evidence nor a public claim.
Convert a lesson into an anonymous repository-owned test, sample, issue, or decision before relying on
it here.

## Initial assessment queue

| Surface | Assessment state | Maturity | Evidence record | Principal question |
|---|---|---|---|---|
| Bootstrap, discovery, and startup reporting | unassessed | — | R02 | Can composition explain itself deterministically? |
| `Entity<T>` data semantics and context | unassessed | — | R02 | Is Entity the coherent first-class language across providers? |
| Backend discovery and negotiation | unassessed | — | R02 | Are choice, fallback, and failure visible and controllable? |
| Web/API conventions | unassessed | — | R02 | How much useful API emerges from business-aligned code? |
| Events and messaging | unassessed | — | R02 | Do event semantics remain discoverable from Entity and compose safely? |
| Jobs and scheduling | unassessed | — | R02 | Is the common path convention-led and operationally inspectable? |
| Cache and distributed state | unassessed | — | R02 | Are defaults, invalidation, and provider boundaries explicit? |
| AI, vector, and semantic capabilities | unassessed | — | R02 | Which agentic workflows are foundations versus extensions? |
| MCP and agent-facing surfaces | unassessed | — | R02 | Can an agent discover, invoke, and diagnose capabilities reliably? |
| Authentication and authorization | unassessed | — | R02 | Are secure defaults and responsibility boundaries unambiguous? |
| Testing and local infrastructure | unassessed | — | R02 | Can developers prove meaningful behavior without bespoke harnesses? |
| Packaging, installation, and upgrades | unassessed | — | R02 | Is a clean checkout reproducible and version-coherent? |
| Operations, health, and diagnostics | unassessed | — | R02 | Can an operator explain what Koan selected and why? |

R02 may split or merge rows only when the resulting boundaries better match user-visible contracts.

## Entry template

```markdown
### <Capability>

- Assessment date and commit:
- Assessor:
- User outcome:
- Shortest supported path:
- Public entry point / package:
- Implementation:
- Automated evidence:
- Maintained example:
- Startup and inspection behavior:
- Supported scenarios:
- Unsupported scenarios:
- Compatibility expectation:
- Maturity:
- Claim safe to publish:
- Open risks:
```

## Promotion rule

A claim may move upward only when all evidence required by its target label is linked and the relevant
work item passes [`ACCEPTANCE.md`](ACCEPTANCE.md). Absence of a known failure is not evidence. A single
private deployment is not evidence. A sample without assertions is demonstration, not verification.
