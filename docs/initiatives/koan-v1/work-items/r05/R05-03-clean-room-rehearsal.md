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
  scope: fresh Git-derived dual-application package rehearsal passed; independent review pending
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
