---
type: ARCHITECTURE
domain: framework
title: "R04 Foundation Hardening Backlog"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: reviewed
  scope: dependency-ordered R04 implementation cards
---

# R04 foundation hardening backlog

This backlog converts R02 evidence and R03 semantic decisions into independently reviewable changes.
It is the execution order, not a wish list. A card may move only when new evidence changes a dependency
or blast-radius assumption and the divergence is recorded in `PROGRESS.md`.

## Sequence

```text
R04-01 honest backup deletion
  └─ independent immediate safety repair

R04-02 host-scoped runtime and repeatable hosts
  ├─> R04-03 bounded bootstrap lanes
  ├─> R04-04 atomic packages and external clean room
  └─> R04-05 one explanation and error fact model
          ├─> R04-06 honest negotiation and bounded relationships
          └─> R04-07 module-grown Entity language and receiver cleanup
                    └─> R04-08 executable first use, agent use, and documentation
```

Packaging can research in parallel with host work, but its release gate cannot pass until repeatable
host/build tests are trustworthy. Entity syntax migration waits for safety, lifetime, and fact-model
foundations so a pleasant API is not laid over ambiguous behavior.

## Cards

| ID | Priority | Status | Meaningful result | Depends on | Evidence |
|---|---|---|---|---|---|
| [R04-01](work-items/r04/R04-01-honest-backup-deletion.md) | P0 | passed | Deleting a backup can no longer report success without deleting anything. | R03 | faulted-task contract and focused executable spec |
| [R04-02](work-items/r04/R04-02-host-scoped-runtime.md) | P0 | in-progress | Repeated hosts/tests cannot inherit disposed providers or registrations. | R04-01 | canonical leases, provider-owned aggregate configuration, scoped Identity/Web startup, and binder-owned data specs are green; conformance/logging residuals remain |
| [R04-03](work-items/r04/R04-03-bounded-bootstrap-lanes.md) | P0 | pending | Bootstrap tests finish or fail with a bounded, diagnostic result. | R04-02 | focused suite silent beyond 304 seconds |
| [R04-04](work-items/r04/R04-04-atomic-packages-clean-room.md) | P0 | pending | One package set restores, builds, runs, and performs CRUD outside the repo. | R04-02, R04-03 | public 0.17.0 dependency mismatch and advisory |
| [R04-05](work-items/r04/R04-05-explanation-error-facts.md) | P1 | pending | Humans, agents, health, and logs project one redacted composition/error fact model. | R04-02, R04-03 | best-effort fragmented startup/health reporting |
| [R04-06](work-items/r04/R04-06-negotiation-boundedness.md) | P1 | pending | Provider selection, fallback, and relationship cost fail or explain themselves honestly. | R04-05 | hidden load-all/filter relationship path; fleet gaps |
| [R04-07](work-items/r04/R04-07-entity-language-consumers.md) | P1 | pending | Referencing a module adds only valid Entity facets; invalid/broad receivers disappear safely. | R04-02, R04-05 | ARCH-0106, C# 14 probe, and elected facet slate |
| [R04-08](work-items/r04/R04-08-first-use-agent-docs.md) | P1 | pending | Clean first use and agent use are executable, truthful, and release-gating. | R04-04–07 | broken package quickstart, stale docs, fragmented agent proof |

## Program rules

- Only one production-code card is normally active.
- Use the repository `explore` skill before each card's first production edit.
- A card lands a meaningful safe state; preparatory machinery without a user-visible proof is split or
  kept inside the same atomic change.
- Public API removal, package publication, compatibility promises, or release/support changes retain
  their operator gates even when implementation is authorized.
- Capability maturity changes only after the ledger's evidence requirements are met.
- Private downstream observations never appear as proof; reproduce anonymously in repository tests.

## R05 entry gate

R05 may begin only when:

- R04-01 through R04-04 pass;
- the fact envelope and negotiation contract used by the golden path pass their R04-05/06 cells;
- the Entity language used by the journey has compile-consumer proof;
- any unfinished R04-07/08 breadth has a bounded exception that cannot silently invalidate the
  journey;
- `CAPABILITIES.md` and public installation/first-use wording match the resulting evidence.
