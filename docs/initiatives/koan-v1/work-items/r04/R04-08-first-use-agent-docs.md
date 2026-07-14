---
type: GUIDE
domain: framework
title: "R04-08 - Make First and Agent Use Executable"
audience: [maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
---

# R04-08 — Make first and agent use executable

- Priority: P1
- Status: `passed`
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

## Result

[`samples/FirstUse`](../../../../../samples/FirstUse/README.md) is now the single public and executable
contract. The application body is one approval entity, one governed controller, and the complete
four-line bootstrap. Its project expresses SQLite and MCP intent; the application owns no repository,
schema, tool handler, health, or runtime-fact plumbing.

One reusable process probe drives both lanes and records eight user-visible steps: startup/health,
operator facts, REST business result, MCP initialization, agent discovery, origin-aware authorization,
dry-run, and an agent write observed through REST. It asserts SQLite election, byte-identical Web/MCP
fact envelopes, remote deletion omission, dry-run non-mutation, and real mutation. Every run owns a
temporary SQLite store and a unique agent identity.

The package compiler copies this exact public directory—not a private approximation—outside the
repository, restores it only from the staged package closure, builds it, runs the same probe, and emits
`first-use-package-evidence.json`. Public package availability remains a separate release fact.

Public first-use, sample, README, agent, and runtime-inspection guidance now points to this contract.
The retired `TestPipeline` authoring guide was replaced with the xUnit v3/`KoanIntegrationHost`
contract. Data.Backup documentation now names the actual APIs and states its unproven memory,
encryption, deletion, progress, cancellation, and adapter/storage boundaries.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-14; working tree at `7249ce72ad836324fc02b289e21f39ddcd3b6290`
- Evidence: `samples/FirstUse`; `FirstUseApplicationProbe`; source and package evidence below.
- Tests / validation: `Koan.Packaging.Tests` 13/13; package-only clean-room restore/build/probe 8/8
  in 5.202s on .NET 10.0.9 / Windows 10.0.26200; Data.Backup 2/2; marked docs examples 4/4;
  docs lint 0 errors / 1541 existing warnings; full solution and diff checks recorded in the live
  progress ledger.
- Unsupported scenarios: current public packages remain unavailable as a coherent install set;
  timings are observations, not bounds; Production MCP requires authentication; external providers,
  hostile clients, and the full stepwise V0-to-V1 business journey remain outside this card.
- Follow-up work: R05 grows this stable first result into meaningful persisted, reachable, reactive,
  and agentic checkpoints without turning it into a feature showcase.
- Reviewer: Codex; maintainer authorized autonomous implementation.
