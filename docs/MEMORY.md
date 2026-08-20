---
type: GUIDE
domain: framework
title: "Koan durable working memory"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-20
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-20
  status: reviewed
  scope: pointer index and durable learnings; owns no state of its own
---

# Durable working memory

Several people and several agents work this tree in parallel, so anything that must outlive one
session belongs here, in the repository, rather than in a single assistant's private store. An
assistant's own memory is a cache; this file is the source.

It **points**. State lives once and decisions live once, in the documents named below — restating
them here would create a second copy to drift. What this file adds is the part that is written
nowhere else: how to work in this tree, and what earlier sessions learned the hard way.

Sensitive or session-scoped notes stay out of git — see [local/README.md](../local/README.md).

## Where current state lives

| Question | Authority |
|---|---|
| What may I change, and by what law? | [CLAUDE.md](../CLAUDE.md) |
| Which agent surface am I in? | [AGENTS.md](../AGENTS.md) |
| What does the framework offer today, and how proven is it? | [docs/reference/product-surface.md](reference/product-surface.md) (generated) |
| Why is it this way? | [docs/decisions/](decisions/) — ADRs are dated records; a later one supersedes or amends, never edits |
| What is deliberately deferred? | [docs/initiatives/koan-v1/POST-CYCLE-TODO.md](initiatives/koan-v1/POST-CYCLE-TODO.md) |
| How does a release happen? | [docs/engineering/nuget-publishing.md](engineering/nuget-publishing.md) |
| What does an application look like? | [samples/README.md](../samples/README.md) |
| Before changing production code | [.codex/skills/explore/SKILL.md](../.codex/skills/explore/SKILL.md) |

## How to work in this tree

- **Verify empirically; do not reason-and-assert.** Probe the real store, read the startup facts,
  run the thing. Several confident claims in this repo's history were wrong in a way one command
  would have caught.
- **`git ls-files` is the authority for what exists.** Ignored `bin`/`obj` left in `src/` make
  retired packages look live. `ls` has produced false conclusions here more than once.
- **Root fix, not spot fix.** Do not drop a capability to the in-memory floor to make a suite green;
  repair the owner. Where two implementations converge, collapse them rather than adding a third.
- **Reference = Intent.** The canonical bootstrap is a bare `AddKoan()`. A sample that needs an
  argument to compose is reporting a framework gap, not configuring itself.
- **Never hand-edit a package version.** Versions come from NBGV; releasing fast-forwards `main`.
- **Fix the seam.** When a feature cannot ride an existing contributor pipeline, the pipeline is what
  needs work — bespoke per-feature logic is how axes drift apart.

## Durable learnings

- **A test double that is more capable than production hides defects.** Canon's persistence double
  returned the object it was handed, so nothing could ever be lost in storage. Every Canon spec
  passed while the pillar's central promise — messy arrivals converge — was broken for every real
  adapter. A double must be honest about the property that makes storage storage: serialization.
  Making it so gave an existing spec teeth it had never had. (2026-08-20)
- **A spec that only compares answers cannot see whether the store did the work.** Every adapter ordered
  `-Sightings.LastChangedAt` correctly while none of them pushed it down: the framework's sorter finished
  the job over the whole materialized result. The surface suites were green throughout. Where pushdown
  matters, assert the adapter's receipt (`SortHandled`, `PaginationHandled`) alongside the ordering — and
  read a receipt before believing a claim about which layer answered. (2026-08-20)
- **A cross-adapter ordering corpus must be a total order.** Ties pin LINQ's stable sort against databases
  that promise nothing among equals, and fail on whichever store breaks them differently — a failure that
  is not a defect. (2026-08-20)
- **A suite that shells out must inherit its own build configuration.** A sample spec pinned
  `-c Release --no-build` while its build produced Debug, so it passed only on a machine where
  someone had previously built that sample by hand. (2026-08-20)
- **Readiness that cannot become green is worse than red.** A probe that reports unhealthy until
  something provisions, in a system where nothing provisions until traffic arrives, deadlocks under
  any orchestrator. Distinguish *not yet written to* from *broken*. (2026-08-20, ARCH-0128 wave)
- **Certification is deliberately manual.** `pr-gate` builds and projects but runs no tests, so red
  suites accumulate unseen between explicit ratchet runs. Run `scripts/green-ratchet.ps1` at a real
  boundary and read `artifacts/ratchet/test-manifest.json`, rather than trusting a green PR.
- **Sample suites are the only proof of the real process.** In-process specs cannot see stdout
  ownership, shutdown, discovery health, or ambient flow across an async hop. Every framework-level
  defect found by running a sample end-to-end was invisible to the in-memory suites.
- **A deferred entry records the tree on the day it was written.** Verify the premise before working
  one; the register's own contract says why.
