---
type: GUIDE
domain: framework
title: "R05-02 - Add Reactive and Agentic Collaboration"
audience: [maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: verified
  scope: Jobs progress and GoldenJourney source process proof
---

# R05-02 — Add reactive and agentic collaboration

- Status: `passed`
- Depends on: R05-01
- Owner: Jobs, MCP, runtime facts, GoldenJourney

## Meaningful result

Assessment runs as durable background work and a remote agent can discover pending reviews and record
a bounded, non-final recommendation. Operators and agents see the same explanation of the composed
runtime.

## Result

`ReviewRequest` implements `IKoanJob<ReviewRequest>`; its handler invokes the same `Assess` rule and
reports completion. Jobs now contributes semantic `jobs:ledger` and `jobs:wake` facts. A defect
found by the running journey—terminal settlement overwriting newer progress—was repaired by updating
the claimed record before ledger persistence; shared in-memory and SQLite tests protect it.

`ReviewTools` exposes only `review_pending` and `review_recommend`. The first pushes the assessed and
pending predicate into a bounded query. The second
calls the domain rule, rejects unassessed or duplicate work with stable business codes, and records a
recommendation without pretending to make the final decision. Custom-tool dry-run remains an honest
non-executing partial rehearsal because imperative effects are not framework-inspectable.

The source process proof verifies Jobs elections, completed progress, byte-identical Web/MCP facts,
tool discovery, pre-assessment rejection, dry-run non-mutation, and an agent result observed through
REST. It also proves an unavailable configured adapter yields a stable rejected fact and correction,
then a clean restart restores SQLite election.

## Acceptance result

- Outcome: PASS
- Tests: Jobs 76/76 in-memory and 78/78 SQLite; GoldenJourney cumulative source proof 1/1.
- Unsupported: distributed transport, hostile-client security, custom-verb full rehearsal, and wire
  compatibility beyond current pre-1.0 behavior remain outside the claim.
