---
type: GUIDE
domain: data
title: "R04-07 - Prove Module-Grown Entity Language"
audience: [maintainers, framework-authors, developers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
---

# R04-07 — Prove module-grown Entity language

- Priority: P1
- Status: `passed`
- Depends on: R04-02, R04-05
- Owner: Data.Core plus one pilot module

## User-visible failure

Optional cache behavior appears in Data.Core even when unavailable; other module extensions need extra
imports; broad persistence/messaging receivers show invalid verbs; type/control-plane operations are
attached to arbitrary instances.

## Personas

Developers and coding agents receive noisy or dishonest IntelliSense; reviewers cannot see module
language drift; maintainers fear cleanup because consumer compilation is not gated.

## Delivered evidence

ARCH-0106 and R03 inventory classify each hazard. The repository-owned .NET 10/C# 14 consumer suite
now proves the static module facet against real built assemblies. The
[`R04 Entity Facet Candidate Slate`](../../R04-ENTITY-FACET-CANDIDATES.md) elects `Cache` as the
consumer-infrastructure pilot, intrinsic `Events` and module-grown `AI` as user-delight flagships, and
narrowly constrained `Media` as the next interface proof; it explicitly keeps control-plane pillars
and generic messaging/jobs off Entity.

`Todo.Cache` is now declared in `Koan.Cache` under the canonical Entity namespace. Data.Core no
longer predicts the member. Existing `Flush/Count/Any` behavior moved intact; read-only `Explain()`
projects materialized policy facts and resolves the current host per call. `Delete(object)` remains
binary/static-call compatible but is no longer an extension receiver and is explicitly obsolete.

## Smallest meaningful fix

Create repository-owned consumer compilation tests for base-absence, module-presence, invalid receiver,
all-module collision, and module removal. Pilot non-destructive `Cache` explanation/inspection without
migrating all modules. Separately deprecate or constrain one broad receiver.

## Failure behavior

Absent modules fail at compile time; missing runtime prerequisites use the R04-05 error facts. Invalid
receivers never reach reflection. Collision tests fail the module build.

## Verification

- [x] C# 14 absence, presence, invalid-receiver, contracted-module collision, and removal cells;
- [x] XML docs describe read-only explanation, host prerequisite, and Entity-type scope;
- [x] no package global-using injection;
- [x] repeated-host resolution uses the active host on every call;
- [x] existing Cache operations forward through the module-owned facet;
- [x] runtime-only `Delete(object)` remains an explicit deprecated static compatibility path.

Acceptance: Entity language 9/9; Data.Core 299/299; Cache topology 50/50; Cache cross-engine 14/14;
Release solution build 0 errors / 25 existing warnings; documentation lint 0 errors / 1541 existing
warnings; `git diff --check` clean.

## Compatibility and rollback

Land test infrastructure before syntax migration. Preserve a forwarding API with `[Obsolete]` where
safe; name replacement and removal condition. Revert the pilot facet without removing consumer gates.

## Stop condition

Do not migrate cache, events, AI, backup, messaging, canon, media, and jobs in one card. One facet and
one broad receiver are the maximum pilot scope.
