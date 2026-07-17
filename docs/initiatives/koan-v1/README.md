---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Initiative"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
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
language that R04-07 proved; it is historical where ARCH-0113 now defines Lifecycle, Events,
Transport, and capability lifting.
R05 execution follows the three-card [`R05-BACKLOG.md`](R05-BACKLOG.md); it preserves FirstUse and
proves meaningful growth in the cumulative [`GoldenJourney`](../../../samples/GoldenJourney/README.md).
R06 and R07 graduate the foundation and semantic capability rings. R09 has now passed and records its
completed composition architecture in the [parent](work-items/R09-semantic-composition-kernel.md),
[backlog](R09-BACKLOG.md), [coalescence inventory](R09-COALESCENCE-INVENTORY.md), and closing
[single-module handoff](work-items/r09/R09-09-one-bootstrap-language-and-release-handoff.md).
R08-01 preserves the completed Git-driven exact release-wave baseline; R08-01 through R08-04 pass locally and
R08-05's public-observation contract is prepared. The completed
[R10 golden-sample graduation](work-items/R10-golden-samples.md) ensures the maintained curriculum teaches
and executes the rebuilt architecture. The active
[R11 package-product graduation](work-items/R11-package-product-quality.md) now ensures every surviving NuGet
package earns and proves a distinct reference intent before that first public wave. See [NOW.md](NOW.md).
Small design and polish debts deliberately kept out of the active acceptance path live in the
[`POST-CYCLE-TODO.md`](POST-CYCLE-TODO.md) register with their required decisions and evidence.

## Sources of truth

| Question | Authoritative artifact |
|---|---|
| What does the initiative believe? | `CHARTER.md` |
| What is the dependency order? | `ROADMAP.md` |
| What is in progress or runnable? | `PROGRESS.md` |
| What should the next session do? | `NOW.md` |
| What exactly does a work item require? | its `work-items/Rxx-*.md` card |
| What records the foundation-repair sequence? | `R04-BACKLOG.md` and its linked historical child cards |
| What records the golden-journey sequence? | `R05-BACKLOG.md` and its linked historical child cards |
| What records the completed semantic-composition work? | `R09-BACKLOG.md`, `R09-COALESCENCE-INVENTORY.md`, and the linked accepted child cards |
| Which small issues wait until the main cycle closes? | `POST-CYCLE-TODO.md` |
| Which module capabilities should grow Entity language? | the Entity Semantics Contract and `ARCH-0113`; the R04 slate is historical input |
| What makes work acceptable? | `ACCEPTANCE.md` |
| How mature is a public capability? | `CAPABILITIES.md` |
| What belongs in the Entity language? | `docs/architecture/entity-semantics-contract.md` |
| What does Koan actually ship? | source, tests, current reference docs |

When artifacts disagree, shipped evidence wins. Record the contradiction in the Progress
Divergence log; do not silently reconcile it by editing multiple narratives.

## Execution sequence

```text
R00 privacy boundary
  -> R01 product constitution
      -> R02 capability baseline
          -> R03 Entity Semantics Contract
              -> R04 foundation-hardening backlog
                  -> R05 golden V0-to-V1 journey
                      -> R06 foundation graduation
                          -> R07 semantic capability ring
                              -> R08-01 durable release-wave baseline
                                  -> R09 semantic composition kernel
                                      -> remaining R08 release readiness
                                          -> R10 golden sample graduation
                                              -> R11 package-product graduation
                                                  -> R08 public observation and V1 decision
```

Later implementation cards are created only when the active parent and preceding evidence establish
their smallest meaningful result. This prevents the initiative from becoming a speculative feature
or abstraction backlog.

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
