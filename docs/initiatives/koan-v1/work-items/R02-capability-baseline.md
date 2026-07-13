---
type: GUIDE
domain: framework
title: "R02 - Build the Capability Truth Baseline"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: capability assessment work-item specification
---

# R02 — Build the capability truth baseline

- Tranche: `T2 — capability truth baseline`
- Status: `passed`
- Depends on: R01
- Unlocks: R03
- Owner: maintainer

## Meaningful outcome

Developers and maintainers can distinguish what Koan specifies, demonstrates, verifies, supports,
deprecates, or does not support. Public claims become traceable to current code and tests.

## Why now

Foundation work must be ranked by evidence, not repository size or documentation confidence. The
current product has substantial capability breadth, but its support boundaries have not been collated
into one current ledger.

## Evidence to read first

- [`../CAPABILITIES.md`](../CAPABILITIES.md).
- Canonical package projects, public entry points, focused tests, maintained samples, and current docs.
- Startup reports, health checks, configuration descriptors, error contracts, and provider selection.

## Decisions

### DECIDED

- Code proves existence; tests prove only the behavior they assert; documentation proves intent.
- Samples demonstrate paths but do not create compatibility guarantees.
- Private downstream use is not citable evidence.
- Unsupported and unassessed are valid, useful findings.

### DEFAULT

- Assess by user-visible capability rather than assembly count.
- Prefer foundation versus supported-extension boundaries over a flat feature checklist.
- Use the commit under assessment as the reproducible snapshot.

### OPEN

- Which capabilities meet each maturity label today?
- Which public documents overstate or understate current behavior?
- Which missing tests prevent a safe support claim?

## Scope

### In

- Populate every initial row in `CAPABILITIES.md`.
- Trace public claims to implementation, tests, and maintained examples.
- Record shortest supported paths and unsupported scenarios.
- Produce a ranked evidence-gap and documentation-correction backlog.

### Out

- Fixing discovered runtime gaps.
- Expanding a capability to make a preferred maturity label true.
- Competitive feature scoring.

## Execution plan

1. Freeze the assessed commit and inventory public capability entry points.
2. Walk each capability vertically from application code through selection, execution, failure, and
   operational evidence.
3. Run focused tests and shortest-path examples where practical.
4. Assign the lowest maturity label fully supported by evidence.
5. Correct materially misleading public wording or create a separately ranked correction card.

## Verification

- Every assessment queue row has a complete evidence record.
- Every maturity label can be reproduced from linked repository artifacts.
- Every significant public claim is classified as supported, directional, overstated, understated, or
  obsolete.
- Commands, environment, failures, and untested scenarios are recorded.

## Acceptance additions

- No initial surface remains `unassessed` without an explicit `BLOCK` and restart plan.
- A maintainer reviews the evidence gaps before R03 begins.

## Stop conditions

- Stop and split the work if a surface is too broad to assess coherently.
- Do not upgrade a maturity label to avoid an uncomfortable documentation correction.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-13; assessed snapshot
  `4471e9c7ffeaa2cd198a62589a9763c4555d9b7f`; acceptance recorded in this closure change.
- Evidence: all 13 capability records in [`../CAPABILITIES.md`](../CAPABILITIES.md), command results and
  public-claim audit in [`../R02-EVIDENCE.md`](../R02-EVIDENCE.md), and ranked dispositions in the same
  evidence record.
- Tests / validation: 900 focused tests passed across data core, web, jobs, cache, AI, vector,
  MCP, identity, testing, and observability; 3 skipped; 1 AI integration test failed; the bootstrap
  suite did not complete within 304 seconds. `S1.Web` builds with 0 errors and 10 warnings. A clean
  exact-0.17.0 NuGet application fails restore because the public package set is version-incoherent.
- Unsupported scenarios: container-backed provider fleet, distributed brokers/transports, external
  identity providers, full-solution certification, package upgrades, and production operations were
  not assessed. Each capability record narrows its own verified scope.
- Follow-up work: R03 owns the Entity admission/IntelliSense and ecosystem contract. R04 begins with
  packaging, host lifecycle, bootstrap testability, and unified explanation.
- Reviewer: maintainer authorized full initiative execution; the conservative evidence labels are
  recorded for repository review rather than inferred from that authorization.
