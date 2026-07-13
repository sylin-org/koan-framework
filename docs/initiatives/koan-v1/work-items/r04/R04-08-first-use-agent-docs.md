---
type: GUIDE
domain: framework
title: "R04-08 - Make First and Agent Use Executable"
audience: [maintainers, developers, operators, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-08 — Make first and agent use executable

- Priority: P1
- Status: `pending`
- Depends on: R04-04, R04-05, R04-06, R04-07
- Owner: documentation, samples, testing, MCP

## User-visible failure

The source path builds but has no honest universal 60-second bound; package quickstarts are currently
blocked; some sample/assessment prose is stale; agent guidance and runtime inspection are not one
executable first-use contract.

## Personas

New developers cannot distinguish target syntax from supported syntax; agents can follow stale code;
operators/reviewers lack a compact proof of composition, effects, and limits.

## Current evidence

R02's build/package/claim audit and R03's Entity/agent contracts. Current front-door wording is
qualified but the intended package journey remains non-runnable until R04-04. The Data.Backup README
names types and workflows that do not exist in current source, while the test-authoring guide mandates
a `TestPipeline` and `tests/Shared/Koan.Testing` path that are no longer present. These are concrete
examples of why executable documentation must replace prose-only confidence.

## Smallest meaningful fix

Create one anonymous application fixture outside framework internals that executes the documented
checkout/package path, adds one business capability per meaningful step, asserts HTTP/business output,
captures the structured explanation, and lets an MCP agent discover/read/mutate only the authorized
surface. Generate or validate snippets/claims from that fixture.

## Failure behavior

The journey reports the exact failed step, package/module/provider intent, fact-envelope correlation,
and corrective action. It never substitutes a showcase transcript for executable proof.

## Verification

- clean source and package lanes with recorded environment/time, but no universal timing promise;
- application code/business-density score and no preparatory scaffolding-only step;
- positive and negative agent operations, authorization, dry-run/effect, stable errors;
- operator/reviewer can inspect composition drift and backend choice;
- front-door links/snippets/docs lint and stale-document status gates.

## Compatibility and rollback

The executable fixture is additive and becomes a release gate only after stable. Documentation changes
must match current package/support state. Roll back generated wording if the fixture fails; never keep a
claim whose executable source was removed.

## Stop condition

R05 owns the full V0-to-V1 product journey. Stop R04-08 at the minimal foundation/agent proof needed to
make R05 trustworthy rather than duplicating it.
