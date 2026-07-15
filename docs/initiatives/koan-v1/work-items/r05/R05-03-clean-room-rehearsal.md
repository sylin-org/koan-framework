---
type: GUIDE
domain: framework
title: "R05-03 - Prove the Clean Room and Independent Rehearsal"
audience: [maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: reviewed
  scope: independent agent review entered bounded repair-and-repeat
---

# R05-03 — Prove the clean room and independent rehearsal

- Status: `in-progress`
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

## Remaining acceptance gate

- Have a new human and a new coding-agent context independently follow the public path; record
  confusion, corrections, and whether business behavior can be reviewed without framework internals.
- Update capability maturity and package-install wording only if the resulting public release state
  supports those claims.

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

R05-03 passes only after both records exist and any material repair has rerun its affected automated
source/package contract.

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
