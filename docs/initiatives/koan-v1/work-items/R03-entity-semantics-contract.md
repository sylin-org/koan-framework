---
type: ARCHITECTURE
domain: framework
title: "R03 - Define the Entity Semantics Contract"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: Entity semantics and ecosystem-boundary work-item specification
---

# R03 — Define the Entity Semantics Contract

- Tranche: `T3 — semantic spine and ecosystem boundaries`
- Status: `pending`
- Depends on: R02
- Unlocks: R04
- Owner: maintainer

## Meaningful outcome

An application developer can begin with `Entity<T>`, add modules, and discover coherent new business
capabilities through IntelliSense without learning disconnected infrastructure vocabularies. Framework
authors know which semantics belong on Entity and which do not.

## Why now

Entity is intentionally first-class for cognitive load and discoverability. As modules grow, that
advantage can become an undifferentiated extension-method surface unless Koan defines an explicit
semantic admission and composition contract.

## Evidence to read first

- R02 capability records for Entity, context, events, cache, web, jobs, AI/vector, and providers.
- Current `Entity<T>` type, extensions, conventions, overloads, tests, and IntelliSense-visible docs.
- Focused primary-source research into ABP and other relevant .NET ecosystem approaches to entities,
  modularity, conventions, repositories, units of work, domain events, and application services.

## Decisions

### DECIDED

- `Entity<T>` is Koan's first-class semantic citizen.
- Modules should add coherent Entity capabilities through discoverable extensions where the operation
  is genuinely entity-centered.
- Koan collaborates with and augments the .NET ecosystem; it does not need feature parity or a
  competitive posture.

### DEFAULT

- Adopt, adapt, integrate, complement, or decline external ideas explicitly.
- Keep infrastructure control planes and application-level workflows off Entity unless evidence shows
  they are intrinsic entity semantics.

### OPEN

- What is the admission test for an Entity extension?
- How are context, transactions, events, authorization, tenancy, validation, and backend choice exposed
  without semantic clutter?
- Which ABP approaches offer strategic value, and where should Koan integrate rather than absorb?
- What naming, overload, namespace, and documentation rules preserve IntelliSense quality?

## Scope

### In

- Specify Entity invariants, capability categories, admission/rejection tests, and namespace rules.
- Map current extensions and identify collisions, gaps, and misplaced operations.
- Define context propagation, backend negotiation, event, and inspection expectations around Entity.
- Produce an evidence-based ecosystem disposition matrix.

### Out

- Implementing every identified API change.
- Recreating ABP or another framework inside Koan.
- Making all module behavior Entity-shaped.

## Business-code proof

Use a small anonymous domain to demonstrate that adding relevant module references makes coherent
capabilities discoverable on the entity while application code stays business-aligned. Include a
counterexample showing a capability deliberately kept elsewhere.

## Verification

- Every current Entity extension category maps to the contract or a remediation item.
- Representative IntelliSense surfaces are reviewed for discoverability and ambiguity.
- Ecosystem conclusions cite current primary sources and state adopt/adapt/integrate/complement/decline.
- The contract covers happy path, failure, inspection, provider differences, and escape hatches.

## Acceptance additions

- A maintainer ratifies the Entity admission test and ecosystem dispositions.
- R04 can rank implementation work without reopening the semantic model.

## Stop conditions

- Stop if capability evidence contradicts the assumed Entity model; update the constitution explicitly.
- Stop ecosystem research when it no longer changes a concrete Koan decision.
