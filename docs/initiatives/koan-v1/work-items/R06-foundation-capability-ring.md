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
- Status: `in-progress`
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

### OPEN

- Which local providers are named in the graduated foundation boundary: InMemory only, InMemory plus
  SQLite, or InMemory/JSON/SQLite with separate durability claims?
- After host isolation is repaired, does composition need a broader proof than the existing
  FirstUse/GoldenJourney and lockfile contracts?
- Which compatibility statement is honest before the first observed public `dev` package set?

## First bounded repair

[`R06-01`](r06/R06-01-conformance-host-isolation.md) makes conformance execution own its ambient host
scope so independent Entity specifications can run concurrently without an assembly-level xUnit
switch. It passes; the parent ring now owns the remaining support-boundary decision.

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
