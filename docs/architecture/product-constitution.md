---
type: ARCHITECTURE
domain: framework
title: "Koan Product Constitution"
audience: [architects, developers, maintainers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: reviewed
  scope: product identity, constitutional principles, contract/implementation boundary, and proposal decision test
---

# Koan Product Constitution

This document defines the durable product rules used to decide what Koan should become. It does not
claim that every current package satisfies them. Current behavior is established by source, tests, and
the generated [product surface](../reference/product-surface.md).

## Promise

Koan is an opinionated meta-framework for agentic .NET applications: the Rails move for an era in
which humans and coding agents build together. It helps an application grow from V0 to V1 through
meaningful, small steps.

Application code should read as the business. Koan takes responsibility for recurring infrastructure
composition, conventions, and integration complexity, then explains the decisions it made. It does not
erase complexity or conceal operational commitments.

## Constitutional principles

### 1. Business code is the product

Entities, policies, relationships, actions, workflows, and business tests should dominate application
code. Framework plumbing, generated layers, and preparatory scaffolding are not business progress.

### 2. Entity is the semantic spine

`Entity<T>` is the first-class application citizen and the natural discovery point for behavior whose
subject is an entity or entity type. Referenced modules may add coherent Entity semantics through
extensions, attributes, interfaces, or projections.

Entity is not a dumping ground. Global infrastructure control, provider administration, and unrelated
application workflows belong on their own explicit surfaces.

### 3. Reference expresses intent

A module reference declares that its capability should participate in composition. Koan owns its
registration, safe defaults, and explanation. Environment-specific commitments—credentials,
availability, topology, durability, security posture, and cost—remain explicit.

Every Koan-defined contract intended for consumption by another module **must** live in an isolated
contract assembly (`*.Core`, `*.Abstractions`, or `*.Contracts`) that contains no activating module.
Functional assemblies contain the implementation and its `KoanModule`. Consumers reference the
contract assembly for vocabulary and the functional assembly only when they intend activation. Koan
does not use `Inert` reference metadata to compensate for a contract placed in an implementation
assembly; that is a package-boundary defect to correct.

Reference = Intent is a product contract, not a mandate for one discovery implementation. Build-time
registries, manifests, and runtime fallbacks may collaborate as long as activation remains predictable
and inspectable.

### 4. IntelliSense is the primary discovery surface

When a module adds entity-centered capability, developers and coding agents should find it where they
already look: on the Entity type, an entity instance, or a small strongly typed adjacent concept.
Namespaces, names, overloads, and XML documentation are therefore product design, not polish.

### 5. Startup explains composition

A Koan application should report what it discovered, selected, defaulted, rejected, and could not
verify. Human logs, machine-readable composition, health, tests, and agent-facing inspection should be
projections of the same facts.

Explanation may be best-effort only when it says so. Missing diagnostics must never be presented as a
successful guarantee.

### 6. Capabilities are honest

Providers declare what they can do. Koan negotiates the required semantics and exposes the result.
Unsupported guarantees fail with actionable context; weaker behavior is used only when it is an
explicit, documented contract—not a silent imitation of parity.

An explicit degraded mode may continue safely, but it must identify what failed and which guarantees
are absent.

### 7. Complexity grows progressively

The common path introduces the fewest concepts needed for the outcome. Advanced guarantees add
proportionate concepts and keep explicit escape hatches. Koan prefers one canonical path per intent;
alternatives must have distinct semantics rather than historical duplication.

### 8. One fact model serves every consumer

Application developers, coding agents, operators, and reviewers need different projections, not
different realities. Composition, backend election, capability, error, health, and policy facts should
be stable enough to inspect in code, logs, tests, tools, and agent protocols.

### 9. Koan collaborates with the ecosystem

Koan builds application grammar and composition; it does not recreate ASP.NET Core, Aspire,
OpenTelemetry, specialist providers, or agent orchestration platforms. For an external approach, choose
and document one disposition: adopt, adapt, integrate, complement, or decline.

### 10. Evidence earns support

Implementation proves existence, samples prove a demonstrated path, and automated tests prove only
what they assert. A public support claim also needs a documented contract, important failure coverage,
known limitations, packaging, and a compatibility expectation.

Private downstream experience can generate questions. Only anonymous, repository-owned evidence can
answer them publicly.

Code presented as an active sample is therefore a product contract, not a scratchpad or historical
illustration. Every active sample must be a golden example of the preferred Koan application grammar,
good .NET practice, a meaningful business result, truthful limitations, and executable evidence. Work
that has not reached that bar stays outside the public curriculum or is archived.

## A meaningful step

A V0-to-V1 step is meaningful when all of the following hold:

1. it adds or strengthens an observable business outcome;
2. the application remains runnable and coherent after the step;
3. required application edits primarily express business intent;
4. existing business rules survive unless the requirement itself changes;
5. infrastructure complexity moved into Koan remains explainable and diagnosable;
6. the step has evidence proportionate to the guarantee it introduces.

Creating folders, generated layers, placeholder abstractions, or configuration that no business
behavior uses does not count as a meaningful step.

## Proposal decision test

Evaluate every framework proposal in this order:

| Question | Continue when… | Reject, split, or redirect when… |
|---|---|---|
| What business outcome becomes easier? | A concrete application outcome is named. | The proposal begins with framework machinery or feature parity. |
| Is Koan the right owner? | It belongs to application grammar, composition, or cross-provider truth. | ASP.NET Core, Aspire, a specialist platform, or the application owns it better. |
| Where is it discovered? | Entity or a small typed adjacent surface is natural. | It adds an unrelated Entity extension or a disconnected vocabulary. |
| Is the step meaningful? | The application is useful at the end of this increment. | Preparatory scaffolding is required before value appears. |
| Can composition explain it? | Selection, defaults, failure, and removal are inspectable. | Behavior depends on silent discovery or fallback. |
| Is backend behavior honest? | Required semantics are negotiated and limitations are visible. | The abstraction implies guarantees a provider cannot supply. |
| What is the evidence and removal path? | Tests, support boundary, and safe removal are concrete. | Existence, a private deployment, or a showcase sample is the only proof. |

A proposal that fails an early question should not be rescued by adding more abstraction.

## Responsibility boundary

Koan owns its canonical application grammar, module participation, common-path defaults, provider
negotiation, diagnostics, and framework compatibility. Applications own their domain policy, secrets,
production topology, deployment and recovery commitments, provider-specific choices, and deliberate
departures from the canonical path.

The framework may automate an application-owned decision, but it may not make the responsibility
invisible.

## What is not constitutional

Package names, provider lists, serializer choices, exact startup formatting, the four-line bootstrap,
source-generation mechanics, current samples, and individual module APIs are important but tactical.
They may change without changing Koan's identity if the principles above remain true.

See the [Entity Semantics Contract](entity-semantics-contract.md) for the admission and responsibility
rules, [framework principles](principles.md) for current implementation patterns, and
[ARCH-0105](../decisions/ARCH-0105-product-constitution.md) for the ratification decision.
