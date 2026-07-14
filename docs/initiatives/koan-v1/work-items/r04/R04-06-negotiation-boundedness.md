---
type: GUIDE
domain: data
title: "R04-06 - Make Negotiation and Relationship Cost Honest"
audience: [maintainers, framework-authors, operators, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
---

# R04-06 — Make negotiation and relationship cost honest

- Priority: P1
- Status: `passed`
- Depends on: R04-05
- Owner: Data.Core and provider test kits

## User-visible failure

Adapter capabilities exist, but not every election/fallback is explained or fleet-tested. Relationship
helpers can load all child records and filter in memory without an explicit bound or capability choice.

## Personas

Developers read concise but misleading code; agents cannot estimate effects/cost; operators see load
after the decision rather than why it happened; reviewers cannot prove provider parity.

## Current evidence

[ARCH-0112](../../../../decisions/ARCH-0112-bounded-relationship-negotiation.md) separates filter
correctness from physical cost and establishes one child-edge executor. Entity, batch, Web, and MCP
now choose native, already-resident InMemory, explicit bounded scan/fallback, or a corrective rejection.

## Smallest meaningful fix

Add required-capability and execution-mode facts to one relationship query path. Push down when the
provider supports it; otherwise fail closed unless the caller explicitly supplies and accepts a bounded
fallback. Feed the R04-05 envelope.

## Failure behavior

An unsupported operation names entity/relationship, selected provider, missing capability, refused
fallback, configured bound, and safe alternative. No silent full scan.

## Verification

- reference/in-memory and one external provider exercise native and absent capability cells;
- ambiguous relationship, missing index/capability, explicit bounded fallback, authorization scope,
  cancellation, and large-cardinality fixtures;
- REST/MCP expansion observes the same limits/facts as in-process access;
- provider matrix derives from executable results, not project inventory.

Accepting evidence on 2026-07-14:

- Data Core passes 299/299. The focused relationship cells prove InMemory selection, SQLite native
  execution, JSON strict rejection and bounded success, no partial result beyond a candidate bound,
  Entity-first overloads, grouping, cancellation, and safe facts;
- Core runtime facts pass focused 7/7, including stable operation-fact replacement, safe recollection,
  and the rule that a
  rejected capability decision remains inspectable without degrading readiness;
- governed Web relationship behavior passes 7/7: related-type visibility remains enforced and an
  exceeded result bound returns 413 plus the same runtime fact;
- MCP relationship visibility passes 2/2 and MCP conformance passes 73/73 through the shared endpoint;
- relevant Release builds complete with zero errors.

## Compatibility and rollback

Current silent fallback may become explicit failure, requiring a documented opt-in migration. Preserve
existing method shape only if it can fail honestly; otherwise add the bounded form and deprecate. Never
roll back to unbounded implicit behavior.

## Stop condition

Split general provider-election schema work from the first relationship vertical if it exceeds one
reviewable capability path.

## Accepted boundary

This card certifies child-edge negotiation for InMemory, JSON, and SQLite and declares execution
profiles across the current record providers. It does not certify fleet performance, index
sufficiency, batched parent lookup, recursive/depth-limited graphs, or a request audit trail. Runtime
facts remain a latest-state snapshot.
