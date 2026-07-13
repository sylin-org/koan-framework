---
type: GUIDE
domain: data
title: "R04-06 - Make Negotiation and Relationship Cost Honest"
audience: [maintainers, framework-authors, operators, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-06 — Make negotiation and relationship cost honest

- Priority: P1
- Status: `pending`
- Depends on: R04-05
- Owner: Data.Core and provider test kits

## User-visible failure

Adapter capabilities exist, but not every election/fallback is explained or fleet-tested. Relationship
helpers can load all child records and filter in memory without an explicit bound or capability choice.

## Personas

Developers read concise but misleading code; agents cannot estimate effects/cost; operators see load
after the decision rather than why it happened; reviewers cannot prove provider parity.

## Current evidence

R02 verifies Data.Core but not the provider fleet. R03 identifies the unbounded relationship fallback
and requires native/streamed/hybrid/in-memory/rejected execution facts.

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

## Compatibility and rollback

Current silent fallback may become explicit failure, requiring a documented opt-in migration. Preserve
existing method shape only if it can fail honestly; otherwise add the bounded form and deprecate. Never
roll back to unbounded implicit behavior.

## Stop condition

Split general provider-election schema work from the first relationship vertical if it exceeds one
reviewable capability path.
