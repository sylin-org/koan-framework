---
type: GUIDE
domain: data
title: "R04-07 - Prove Module-Grown Entity Language"
audience: [maintainers, framework-authors, developers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-07 — Prove module-grown Entity language

- Priority: P1
- Status: `pending`
- Depends on: R04-02, R04-05
- Owner: Data.Core plus one pilot module

## User-visible failure

Optional cache behavior appears in Data.Core even when unavailable; other module extensions need extra
imports; broad persistence/messaging receivers show invalid verbs; type/control-plane operations are
attached to arbitrary instances.

## Personas

Developers and coding agents receive noisy or dishonest IntelliSense; reviewers cannot see module
language drift; maintainers fear cleanup because consumer compilation is not gated.

## Current evidence

ARCH-0106 and R03 inventory classify each hazard. A disposable C# 14 probe proved static and instance
extension members on a constrained Entity subtype. The
[`R04 Entity Facet Candidate Slate`](../../R04-ENTITY-FACET-CANDIDATES.md) elects `Cache` as the
consumer-infrastructure pilot, `AI` as the flagship, and narrowly constrained `Media` as the next
interface proof; it explicitly keeps control-plane pillars and generic messaging/jobs off Entity.

## Smallest meaningful fix

Create repository-owned consumer compilation tests for base-absence, module-presence, invalid receiver,
all-module collision, and module removal. Pilot non-destructive `Cache` explanation/inspection without
migrating all modules. Separately deprecate or constrain one broad receiver.

## Failure behavior

Absent modules fail at compile time; missing runtime prerequisites use the R04-05 error facts. Invalid
receivers never reach reflection. Collision tests fail the module build.

## Verification

- C# 14 compile cells from the Entity contract;
- XML docs and agent schema align on effects/prerequisites;
- no package global-using injection;
- repeated-host behavior passes R04-02 fixtures;
- compatibility forwarding/deprecation tests for the migrated member.

## Compatibility and rollback

Land test infrastructure before syntax migration. Preserve a forwarding API with `[Obsolete]` where
safe; name replacement and removal condition. Revert the pilot facet without removing consumer gates.

## Stop condition

Do not migrate cache, AI, backup, messaging, canon, media, and jobs in one card. One facet and one broad
receiver are the maximum pilot scope.
