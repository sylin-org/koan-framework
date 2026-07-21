---
type: SPEC
domain: framework
title: "R06 - Graduate the Foundation Capability Ring"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: first T6 capability-ring assessment and execution boundary
---

# R06 — Graduate the foundation capability ring

- Tranche: `T6 — capability-ring graduation`
- Status: `passed`
- Depends on: R05
- Unlocks: later capability rings and T7 release readiness
- Owner: Core, Data.Core, Testing, and packaging boundaries

## Meaningful outcome

A developer can begin with `AddKoan()` and `Entity<T>`, persist and query meaningful business state,
inspect the composition Koan selected, and inherit trustworthy tests without adding repositories,
provider registration, host plumbing, or assembly-wide test restrictions.

## Initial capability assessment

The ring is strong but not yet graduated.

| Surface | Current evidence | Assessment |
|---|---|---|
| Entity language | Data.Core 301/301; R03 contract; module absence/presence/collision cells; FirstUse and GoldenJourney | The core Entity model is verified within its stated provider-neutral boundary. |
| Local data | InMemory, JSON, and SQLite selection/rejection cells; SQLite source/package journeys | Useful paths are proven, but the foundation provider/support boundary still needs one explicit record. |
| Composition | `AddKoan()`, schema-1 facts, matched lockfiles, warning-clean source apps, fresh package clean rooms | The mechanism is coherent; public package availability remains a T7 release fact. |
| Testing | Reusable conformance batteries, bounded bootstrap/package lanes, and concurrent same-Entity host isolation | R06-01 removes the assembly-wide scheduling leak; final maturity still follows the ring support-boundary assessment. |

The initial result was `repair-and-assess`, not a reason to broaden the work. R06-01 removes the
testing leak. The active assessment now names the exact foundation support boundary and determines
whether any other lower-ring gap is material.

## Decisions

### DECIDED

- The foundation ring is the smallest business-capable Koan substrate, not every data provider or
  every Entity-adjacent module.
- Package publication belongs to T7. T6 must still prove that one staged package closure carries the
  same contract as source.
- A consumer test kit may choose isolation internally; it may not require an application to serialize
  its entire test assembly merely to use Koan's common path.
- Conditional capability batteries may report explicit skips. Missing ambient host ownership must
  never turn into a skip or cross-test contamination.

### RESOLVED

- SQLite owns the durable Level-1 application path; InMemory owns the ephemeral conformance role;
  JSON remains the zero-infrastructure bundled fallback without inheriting the durable claim.
- Existing FirstUse/GoldenJourney source/package and lockfile contracts are sufficient for this ring;
  remote providers and production progression require later evidence.
- Before an observed public `dev` package set, the ring is a verified pre-1.0 candidate boundary, not
  a public `supported-foundation` or compatibility promise.

## Bounded repairs

[`R06-01`](r06/R06-01-conformance-host-isolation.md) makes conformance execution own its ambient host
scope so independent Entity specifications can run concurrently without an assembly-level xUnit
switch. It passes; the parent ring now owns the remaining support-boundary decision.

[`R06-02`](r06/R06-02-foundation-support-boundary.md) replaces the stale public Data catalogue with
the exact Entity/local-provider contract and repairs the InMemory package front door. It passes.

## Exit gate

- `Entity<T>`, Entity context, selected local data providers, `AddKoan()` composition, runtime facts,
  lockfile behavior, and the testing kit have one explicit support boundary.
- The common conformance path needs only an Entity-specific subclass and valid sample factory; it does
  not require application-wide scheduling or host-management ceremony.
- Concurrent specs cannot resolve, mutate, or dispose another spec's host, provider, partition, or
  temporary storage.
- The chosen source and staged-package paths execute the same meaningful Entity contract.
- Startup, error, and inspection evidence names selected/defaulted/rejected behavior.
- Unsupported providers, transactions, concurrency, schema evolution, upgrades, and production
  guarantees are explicit and left to their proper rings.
- `CAPABILITIES.md` changes maturity only as far as packaging and compatibility evidence permit.

## Stop conditions

- Stop a slice that attempts to make every Koan test project parallel-safe at once.
- Stop if isolation requires application-owned service-provider routing or a second Entity API.
- Split provider-specific correctness from the provider-neutral foundation contract.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-15; implementation through `caae9c94`, support-boundary closure recorded by
  the following documentation commit.
- Evidence: R03 Entity contract; Data.Core 301/301 baseline; InMemory 55/55, SQLite 15/15, JSON 14/14;
  FirstUse/GoldenJourney source and staged-package contracts; R06-01 concurrent host proof; canonical
  Data foundation reference.
- Tests / validation: R06-01 red/green and focused host suites; current local connector Release
  assemblies 84/84 combined; strict full-site docs; diff validation.
- Unsupported scenarios: public package installation, remote-provider certification, production
  schema/migration/recovery, cross-provider transactions, and compatibility guarantees.
- Follow-up work: T7 observes/polices package publication; the next T6 ring assesses events, context,
  and isolation without inheriting unsupported production claims.
- Reviewer: Codex under the maintainer's standing autonomous approval.
