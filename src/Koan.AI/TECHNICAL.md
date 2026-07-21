---
uid: reference.modules.Koan.ai
title: Koan.AI - Technical Reference
description: AI provider compilation, source routing, health, and semantic operations.
packages: [Sylin.Koan.AI]
source: src/Koan.AI/
---

## Ownership

`Sylin.Koan.AI` owns the concern-level provider plan, adapter catalog, source routing, health observation, recipes,
category policy, and the `Client` facade. Provider packages own only protocol translation, exact options, discovery
health qualification, and a thin activation description. Core owns generic service discovery and semantic
contribution scheduling.

This separation gives every provider the same lifecycle:

1. Referenced Koan modules contribute provider id plus activator type during composition.
2. Core compiles those contributions into one deterministic `AiProviderPlan`.
3. AI activates providers once at startup and validates id agreement, DI ownership, and source/provider agreement.
4. Only after every activation succeeds does AI publish the adapter catalog and provider sources.
5. Routing elects a source by capability, then resolves that source's provider adapter.

Duplicate provider ids or activator types fail composition. Adapters must be DI-owned singletons; disposable provider
resources therefore have one host lifetime rather than an unmanaged process lifetime.

## Sources and routing

An `AiSourceDefinition` is a policy-bearing collection of endpoint members. Source priority and policy select among
eligible sources and members; adapter registration does not perform endpoint election. Explicit named sources can be
declared under `Koan:Ai:Sources`. Provider-owned default sources use deterministic provider ids and member names.

The router handles capability and category intent, optional source/model hints, recipe bindings, member health, and
adapter resolution. A capability declaration means the adapter implements that operation; it does not guarantee a
particular model is installed or healthy.

Background health observation updates member state without rebuilding provider topology. Startup provenance reports
configured categories/sources, the live adapter roster, and source/member status.

## Host contract

`IAiPipeline` is a composed-host singleton. `Client` resolves it from the active host at operation time and does not
retain a process-global provider. Optional probes return absence; required operations distinguish missing host,
disposed host, and missing pipeline with `KoanHostContextException`.

## Extension contract

Provider authors implement narrow interfaces from `Sylin.Koan.AI.Contracts`, register the adapter as a singleton in
a normal `KoanModule`, and contribute one runtime activator through `AiProviderContributionTarget`. The activator
describes what is usable in this host; it does not mutate a global registry. Endpoint-backed providers should use the
AI-owned source builder and Core discovery rather than duplicating topology, precedence, probing, or naming policy.

Contracts that other modules may consume remain in the isolated contracts package. Referencing those contracts alone
activates no AI runtime.
