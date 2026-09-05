---
type: PLAN
domain: framework
title: "Application evolution initiative"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: approved initiative design; execution findings live in PROGRESS and linked receipts
---

# Application evolution

## Charter

Can Koan help people and agents evolve several applications from shared expertise, with less
repeated architectural work? Test that question through one shared foundation, two small
applications, and subsequent changes attempted by independent teams.

The maintainer approved this initiative on 2026-09-05. The first deliverable is its execution
plan. The first implementation milestone is a shared foundation consumed by two applications.
The working domain is an approval desk, followed by expense requests. Distinct workflows make
reuse observable; two copies of one application would not establish it.

Read this charter, [PROGRESS.md](PROGRESS.md), [NOW.md](NOW.md), then the selected work item.
PROGRESS is the only live status ledger. This page owns purpose, dependencies, acceptance, and
completion; NOW carries restart instructions. Work items own scope and link their evidence.

## Opportunities and hypotheses

| Opportunity | Existing foundation | Improvement to test | Evidence still needed |
|---|---|---|---|
| Shared expertise across mixed-experience teams | Bundles select capabilities; modules and application boundaries implement policy | Package shared contracts, policy, and human/agent guidance for two applications | Less repeated senior intervention; a shared policy update works in both consumers |
| Agent-assisted evolution | Capability guidance, skills, exact identifiers, and application evidence | Repeatable changes to an established application with preserved contracts | Accepted changes, regressions, intervention, elapsed time, and measured agent cost |
| Governed agent access | Entity HTTP and MCP projections with access rules | A complete workflow with permitted and forbidden actions across both entry points | Consistent authorization, tenant scope, and business policy under actual callers |
| Explainable change review | Build composition, runtime facts, health, and behavior checks | A concise review report joining evidence from one change | A reviewer can identify the change, actual provider participation, and unproved assumptions |
| Complete business outcomes | Recipes, solution compositions, and runnable samples | A usable approval application that grows through meaningful requirements | A newcomer completes a second change using the published guidance |
| Incremental adoption | Documented coexistence with existing ASP.NET Core applications | One bounded adoption example and an Aspire coexistence investigation | Preserved contracts, explicit ownership, operating requirements, and a demonstrated rollback |

These are strategic hypotheses. Current capability guarantees remain owned by the
[product surface](../../reference/product-surface.md), source, tests, and current guidance.
Availability of a mechanism does not establish adoption demand or a productivity advantage.

## Ownership and existing work

- [A03](../announcement/work-items/A03-flagship-demo.md) retains ownership of the flagship
  application and recording. Use its approval desk as the first consumer; record its canonical
  source path and revision before extending it. AE-01 owns the shared foundation and second
  consumer. Changes to the flagship use the same source and are coordinated through A03.
- A03 can deliver its runnable baseline before the recording. The announcement's existing
  acceptance criteria determine launch readiness; independent pilots, Aspire findings, and
  completion of this initiative add no launch prerequisite.
- [A11](../announcement/work-items/A11-terseness-receipt.md) owns the line-count comparison.
  Pin the demo revision used for that receipt so later evolution does not silently change it.
- Reuse the [agent evaluation infrastructure](../../../evals/agent-race/README.md) and
  [skill rubric](../../../evals/koan/rubric.md) where applicable. Record reuse and gaps before
  adding tooling. The existing benchmark campaign retains its ownership and publication
  boundary; this initiative does not publish its private findings or reinstate retired cards.
- Framework defects go to their existing owner. Production changes follow
  [CLAUDE.md](../../../CLAUDE.md) and its exploration workflow. Successful experiments feed
  canonical recipes, guides, and focused tests; architectural decisions are recorded when a
  framework decision is actually made.

## Dependency order and milestones

| Stage | Work | Exit evidence |
|---|---|---|
| Shared foundation | [AE-01](work-items/01-shared-foundation.md), using A03's runnable baseline | Two distinct consumers, one shared policy owner, and a verified foundation update |
| Meaningful changes | [AE-02](work-items/02-application-evolution.md) and [AE-03](work-items/03-governed-agent-workflow.md), after AE-01 | Repeatable evolution tasks and actual HTTP/MCP allow-and-deny behavior |
| Review and adoption | [AE-04](work-items/04-change-review.md), after AE-02 and AE-03; [AE-05](work-items/05-incremental-adoption.md), after AE-01 | An assessable change report and a bounded adoption/rollback example; an explicit Aspire disposition |
| Internal decision | Findings from AE-01 through AE-05 | Record which hypotheses merit independent trials and which should be revised or stopped |
| Independent validation | [AE-06](work-items/06-independent-validation.md), after that decision | Participant observations, limitations, and a continue/refine/stop decision for each tested hypothesis |

Default to one implementation card at a time. Each stage must produce a useful artifact even
if a later hypothesis fails. Retain the result of an unsuccessful investigation; an explicit
unsupported or deferred Aspire outcome is a valid disposition, not compatibility evidence.

## Acceptance and measurements

Each completed card links its deliverable, reproduction steps, result, and material limits in
PROGRESS. Store safe reproducible receipts beside their application or under this initiative's
`evidence/` directory when produced. Name actual files in the ledger; create no empty receipts.

- Declare observable success and preserved contracts before attempting a change. Check the
  complete relevant action surface, including negative behavior that could reveal a bypass.
- Record the starting and ending revisions, exact resolved package versions, configuration
  requirements, and provider participation. Build-time composition and runtime facts answer
  different questions; a static lockfile cannot establish runtime election or enforcement.
- Measure time to an accepted change, reviewer minutes, corrective interventions, and contract
  failures. For agents also record model, harness, guidance revision, and cost/token data when
  available. Mark missing measurements as unavailable, never zero.
- Keep unsuccessful attempts and setup time visible. Separate maintainer rehearsals, automated
  agent runs, and independent human observations. With a comparison, use equivalent tasks and
  conditions and state the sample size and limitations; small pilots establish no population claim.
- Use checks that detect a named failure and explain the correction. Documentation-only edits
  need relevant link, structure, and consistency checks; behavioral work needs focused executable
  proof. Full certification belongs at the repository's existing release boundary.
- Public findings follow the [announcement acceptance contract](../announcement/ACCEPTANCE.md).
  Participation and publication consent are explicit; private identities, applications, and
  raw traces stay outside public artifacts. Outreach or publication remains a separate action.

## Completion and durable outputs

After the internal milestone, record whether independent validation is justified and feasible.
Three independent teams is a recruitment target, not a claim or a dependency already satisfied.
If volunteers are unavailable, record that limit and decide to defer or close the pilot phase.

Close when every hypothesis has an evidence-linked disposition, reusable findings have reached
their canonical owners, and remaining work has a named owner. Archive the initiative through the
existing initiative convention, updating incoming links. Further work requires a bounded new
decision, rather than keeping an indefinite program open.

This initiative adds no support SLA, paid program, universal provider guarantee, or productivity
multiplier. Public positioning grows only from the outcomes actually demonstrated.
