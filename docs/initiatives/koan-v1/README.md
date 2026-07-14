---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Initiative"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: verified
  scope: initiative artifact structure and links
---

# Koan V1 Reorganization Initiative

Koan is moving toward a stable product reality:

> **Ruby on Rails for agentic .NET:** an opinionated meta-framework that takes applications from
> V0 to V1 in meaningful, small steps. Application code expresses the business; Koan contains and
> explains the infrastructure complexity.

This initiative turns that direction into a gated execution program. It does not declare Koan 1.0
released, promise a date, or make draft decisions canonical. It creates the evidence and stable
foundations required before a V1 commitment is responsible.

## Read in this order

1. [`CHARTER.md`](CHARTER.md) — mission, invariants, privacy boundary, and session protocol.
2. [`NOW.md`](NOW.md) — the current handoff and the next safe action.
3. [`PROGRESS.md`](PROGRESS.md) — authoritative work-item state and readiness.
4. The selected card under [`work-items/`](work-items/TEMPLATE.md).
5. [`ACCEPTANCE.md`](ACCEPTANCE.md) — the gate that decides whether the card is complete.

Use [`ROADMAP.md`](ROADMAP.md) for dependency order and tranche exit criteria. Use
[`CAPABILITIES.md`](CAPABILITIES.md) for public capability maturity. Neither duplicates live
work-item status.

Completed tranche evidence remains restartable: [`R02-EVIDENCE.md`](R02-EVIDENCE.md) records the
capability baseline; [`R03-ENTITY-INVENTORY.md`](R03-ENTITY-INVENTORY.md) and
[`R03-ECOSYSTEM.md`](R03-ECOSYSTEM.md) support the canonical
[Entity Semantics Contract](../../architecture/entity-semantics-contract.md). R04 execution follows
the dependency-ordered [`R04-BACKLOG.md`](R04-BACKLOG.md); each child card under
[`work-items/r04/`](work-items/r04/) must land an independently meaningful result. The
[`R04-ENTITY-FACET-CANDIDATES.md`](R04-ENTITY-FACET-CANDIDATES.md) slate elects the pillar-owned Entity
language that R04-07 should prove without authorizing a mass API migration.

## Sources of truth

| Question | Authoritative artifact |
|---|---|
| What does the initiative believe? | `CHARTER.md` |
| What is the dependency order? | `ROADMAP.md` |
| What is in progress or runnable? | `PROGRESS.md` |
| What should the next session do? | `NOW.md` |
| What exactly does a work item require? | its `work-items/Rxx-*.md` card |
| What foundation repair executes next? | `R04-BACKLOG.md` and its linked child card |
| Which module capabilities should grow Entity language? | `R04-ENTITY-FACET-CANDIDATES.md`, subject to the Entity Semantics Contract |
| What makes work acceptable? | `ACCEPTANCE.md` |
| How mature is a public capability? | `CAPABILITIES.md` |
| What belongs in the Entity language? | `docs/architecture/entity-semantics-contract.md` |
| What does Koan actually ship? | source, tests, current reference docs |

When artifacts disagree, shipped evidence wins. Record the contradiction in the Progress
Divergence log; do not silently reconcile it by editing multiple narratives.

## Initial sequence

```text
R00 privacy boundary
  -> R01 product constitution
      -> R02 capability baseline
          -> R03 Entity Semantics Contract
              -> R04 foundation-hardening backlog
                  -> R05 golden V0-to-V1 journey
```

Later implementation cards are created only after R04 ranks the foundations and R05 defines the
acceptance journey. This prevents the initiative from becoming a speculative feature backlog.

## Completion and archive

The initiative completes when:

- its constitution is reflected in accepted architecture canon;
- foundation capabilities meet the public maturity gate;
- the golden journey passes from a clean checkout;
- package, upgrade, test-isolation, error, and inspectability contracts are stable;
- remaining experimental surfaces have an explicit disposition;
- V1 release criteria are either met or transferred to a smaller release checklist.

On completion, preserve the final charter, capability snapshot, and outcome report under
`docs/archive/initiatives/`; remove volatile handoff material from the active documentation tree.
