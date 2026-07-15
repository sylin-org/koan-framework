---
type: GUIDE
domain: framework
title: "R05-03 - Prove the Clean Room and Independent Rehearsal"
audience: [maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: package clean room, independent rehearsals, and repair queues accepted
---

# R05-03 — Prove the clean room and independent rehearsal

- Status: `passed`
- Depends on: R05-02
- Owner: packaging, documentation, acceptance

## Meaningful result

A release cannot pass merely because repository project references hide package defects. FirstUse
and GoldenJourney must both restore, build, run, and produce distinct evidence outside the checkout
from the staged independently versioned package closure.

## Implemented

The package compiler now copies both public samples into isolated directories, hydrates one local
Koan feed, and supplies the independently selected App, SQLite, Jobs, and MCP versions to each build.
FirstUse retains its eight-step evidence. GoldenJourney runs its eleven source-shaped checks and
writes a separate `golden-journey-package-evidence.json`. Shared probe infrastructure owns process
lifecycle, temporary SQLite state, bounded readiness, MCP negotiation, failure logs, and cleanup.

A disposable local rehearsal layered current Data.Core, Jobs, and Web packages over the later R04-08
verified closure. External FirstUse passed 8/8 in 4.793s and GoldenJourney passed 11/11 in 10.242s;
both restored and built with zero warnings/errors. An earlier attempt over the older R04-04 closure
failed loudly at its stale Data.Abstractions ABI, confirming that mixed closures cannot produce a
false green.

The authoritative local acceptance then captured the complete working tree as one disposable Git
commit without changing the branch, index, or published refs. From a clean clone at that commit, the
release compiler evaluated 113 independent package owners, selected 84 packages (45 changed versions
and 39 unpublished-current registry repairs), packed and verified all 84, and hydrated a package-only
feed. External FirstUse passed 8/8 in 4.129s and GoldenJourney passed 11/11 in 8.769s; both restored and
built with zero warnings and zero errors. The retained ignored evidence lives under
`artifacts/r05-fresh-release-540a84c9b433/` and names source commit
`540a84c9b4339458c69362cbd1c0aae8b8bc4668`.

## Acceptance basis

The maintainer accepted the accumulated evidence as sufficient on 2026-07-15. Two independent agent
readers, a context-free repeat, repeated clean-room package runs, executable source/package
contracts, and ongoing maintainer review and dogfeeding all tested the same experience from different
angles. Their material findings produced two bounded repair queues, and every promoted repair was
rerun against its affected contract.

An additional scripted human walkthrough would repeat the same maintained path without adding a
meaningfully independent signal. R05 therefore requires explicit maintainer acceptance of evidence
sufficiency, not a ceremonial reader identity. Future independent readers remain valuable discovery
inputs; they are not a permanent release checkbox when current evidence already triangulates the
claim.

The fresh local release proves artifact coherence, not public availability. Public install wording
remains gated until the `dev` workflow publishes and the registry exposes the resulting identities.

## Independent rehearsal record

Give each reader a new checkout and only the repository's public front door. Do not explain the
intended route or provide this session's implementation context. For each reader, record:

- start/end time and the first document or command they chose;
- the first meaningful business result they reached;
- every wrong turn, unclear term, hidden prerequisite, or corrective action;
- whether application code could be reviewed as business behavior without framework archaeology;
- whether startup, Web, and MCP facts explained the composed backend and a forced rejection; and
- `pass`, `repair-and-repeat`, or `stop`, with the smallest responsible repair if needed.

Independent records must identify their coverage and limits. Any material repair must rerun its
affected automated source/package contract before its evidence can support acceptance.

### Fresh independent agent repeat

A new context over `47ce8915` again reproduced FirstUse, GoldenJourney, persistence, modern HTTP MCP,
facts convergence, agent mutation, and forced adapter recovery, then returned `repair-and-repeat` on
a smaller truth queue. Four bounded shared repairs now close it:

1. canonical field paths make the documented camelCase REST filter return the persisted FirstUse
   result across the shared relational translation boundary (`f66bc8f5`);
2. runtime composition uses executable assembly identity for `app`, excludes it from `modules`, and
   both journeys require the matched lockfile fact (`88e3be69`);
3. current docs name `/health/live` and `/health/ready`, rejected adapter intent returns readiness
   503, and Jobs paces persistent ledger failures without repeated Error noise (`46c523d8`); and
4. convention-based MCP schemas are quiet, mutating custom tools advertise `dry_run`, and the HTTP
   guide is rebuilt around the canonical Streamable path (`0e40b455`).

Verification for the final slice passes Koan.Mcp warning-as-error, MCP conformance 74/74,
Streamable HTTP 18/18, both executable journey classes 3/3, and strict docs. Compatibility-sensitive
MCP casing and legacy option names remain explicit post-cycle decisions, not hidden changes. The
independent repeat and its repair queue are complete.

### First independent agent round

Two clean, detached readers evaluated the exact candidate. The deeper reader reproduced the public
shortest path, cumulative business journey, persistence, Jobs, MCP mutation, and byte-identical Web/
MCP facts, then returned `repair-and-repeat`. The second reader returned `pass` over a narrower path
and independently confirmed business readability, composition delight, the missing public V5
reproduction, and warning noise; it did not exercise MCP resources or readiness deeply enough to
close the first reader's findings.

The responsible repair queue is:

1. readiness must ignore available-but-unused connectors;
2. `koan://self` must acknowledge live custom tools;
3. build-time lockfile wording and behavior must agree on supported source and package paths;
4. the V5 rejection/recovery command and routing boundary must be reproducible from public docs;
5. genuine warning noise and minor MCP/REST transport guidance should be tightened.

Repair 1 is complete in local commit `977f33b9`. It introduces selection-aware data health, aligns
JSON readiness with repository auto-provisioning, repairs host-owned observed-Entity diagnostics, and
makes both executable application probes gate on `/health/ready`. Verification passes Data.Core
301/301, JSON 19/19, both source journeys, strict docs, and the 113-owner package inventory.

Repair 2 is complete in local commit `c9977361`. One caller-aware custom-tool projection now governs
protocol listing, remote dispatch, Explorer, and `koan://self`; trusted local STDIO retains its
established surface, while concrete remote callers honor authentication, scopes, and disabled
operational toolsets. `koan://self` now exposes structured custom workflows and matching prose, while
`koan://entities` stays Entity-specific. Verification passes MCP conformance 73/73, the real
Streamable HTTP GoldenJourney source contract 1/1, and strict docs. A fresh agent repeat and human
record remain required after the bounded queue.

Repair 3 is complete in local commit `a2780672`. Core's existing deterministic emitter now ships as
a `buildTransitive` asset, so the App bundle carries Reference = Intent through its package graph;
the two supported source contracts import that same target centrally because ProjectReference cannot
carry NuGet build assets. Both checked-in lockfiles are now executable evidence, both application
probes validate identity and required modules, and the release compiler rejects a Core artifact that
lacks the target. Packaging passes 16/16, Core lockfile tests 4/4, strict docs and the 113-owner
inventory pass. A fresh 84-package clean room restored and built both external applications with zero
warnings/errors: FirstUse passed 8/8 in 4.755s and GoldenJourney passed 11/11 in 8.754s, each recording
`compositionLockfileObserved=true`. Evidence is under ignored `artifacts/r05-lockfile-release/`.

Repair 4 is complete in local commit `775d5716`. GoldenJourney now publishes one exact command that
executes the complete 11-step contract and a manual PowerShell sequence that forces
`Koan:Data:Sources:Default:Adapter=not-referenced`, reads the stable rejection fact and correction,
clears the intent, and observes SQLite recovery. The public explanation pins the routing boundary:
ambient and Entity-specific routes precede the application Default, so `ReviewRequest`'s explicit
`[DataAdapter("sqlite")]` remains scoped while the bad default stays visible. The documented command
passes 1/1 and strict docs passes.

Repair 5 is complete in local commit `ffc1ed27`. The eight compiler warnings traversed by the public
source path are corrected without suppressions, and both supported application contracts now build
with warnings treated as errors. The MCP package front door teaches Reference = Intent and modern
Streamable HTTP: JSON-RPC and initialization use `POST /mcp`; `GET /mcp` is optional established-
session server push; the legacy `/sse` plus `/rpc` shape is explicit opt-in. Root, quickstart, and
FirstUse docs include one SQLite-verified URL-encoded JSON `filter` request. FirstUse passes 1/1,
GoldenJourney's exact public command passes 1/1, the focused SQLite filter contract passes 1/1, and
strict docs pass. Broader small design debts, including the intentional `JobMetric.Count` collision,
are retained with required decisions and evidence in
[`POST-CYCLE-TODO.md`](../../POST-CYCLE-TODO.md) rather than widening R05.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-15; evidence through `0e40b455`, closure recorded by the following
  documentation commit.
- Evidence: fresh 84-package clean rooms; FirstUse and GoldenJourney source/package contracts; two
  independent agent evaluations; one context-free repeat; two completed repair queues; explicit
  maintainer acceptance of evidence sufficiency.
- Tests / validation: external FirstUse 8/8 and GoldenJourney 11/11; Jobs 76/76; MCP conformance
  74/74; Streamable HTTP 18/18; focused executable journeys 3/3; affected warning-as-error builds;
  strict documentation validation.
- Unsupported scenarios: the unpublished public package closure, distributed Jobs transports,
  hostile-client security, every adapter/provider, full custom-tool rehearsal, and production
  authorization design.
- Follow-up work: observe a real `dev` publication before promoting package maturity; assess the T6
  capability rings; retain bounded design and polish issues in `POST-CYCLE-TODO.md`.
- Reviewer: maintainer and Codex; the maintainer explicitly ratified evidence sufficiency on
  2026-07-15.
