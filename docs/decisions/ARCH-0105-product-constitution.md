---
id: ARCH-0105
slug: product-constitution
domain: Architecture
status: Accepted
date: 2026-07-13
title: Koan product constitution and proposal decision test
---

# ARCH-0105: Koan product constitution and proposal decision test

## Context

Koan's public README, architecture principles, assessment, ADRs, and current code agree on a strong
center: Entity-first application language, Reference = Intent, capability honesty, progressive
complexity, and a self-describing runtime. They do not consistently distinguish enduring product rules
from current mechanisms or maturity claims.

Examples of the resulting drift include describing source-generated discovery as if no runtime
fallback exists, presenting fail-loud policy as universal current behavior, treating best-effort
startup reporting as complete, and allowing the existence of broad module surfaces to imply support.
Those statements weaken the genuinely distinctive product idea.

The V1 reorganization needs a stable decision frame before assessing capabilities or changing APIs.

## Decision

Adopt [`docs/architecture/product-constitution.md`](../architecture/product-constitution.md) as the
canonical product decision frame.

Koan is an opinionated meta-framework for agentic .NET applications that moves applications from V0 to
V1 in meaningful, small steps. Application code should read as business; Koan owns recurring
infrastructure composition and makes that complexity inspectable.

The constitutional principles are:

1. business code is the product;
2. `Entity<T>` is the semantic spine;
3. references express participation intent;
4. IntelliSense is the primary application-facing discovery surface;
5. startup explains composition;
6. provider capabilities are negotiated honestly;
7. complexity grows progressively through one canonical path and explicit escape hatches;
8. humans, agents, operators, and reviewers consume projections of one fact model;
9. Koan collaborates with the .NET and agent ecosystems;
10. repository evidence earns public support.

### Classification of earlier principles

| Earlier idea | Disposition |
|---|---|
| Entity-first development | Constitutional; narrowed to genuinely entity-centered semantics. |
| Reference = Intent | Constitutional; a reference declares participation, not infrastructure availability or every production guarantee. |
| Provider transparency and capability negotiation | Constitutional. |
| Fail loud | Constitutional as actionable visibility; explicit degraded modes remain valid when their missing guarantees are reported. |
| Self-reporting runtime | Constitutional direction; current reports remain subject to capability evidence. |
| Progressive complexity, canonical paths, escape hatches | Constitutional. |
| Integration tests and written decisions | Evidence policy supporting the constitution. |
| Four-line bootstrap, Roslyn registry, Newtonsoft.Json, provider lists, exact report format | Tactical and replaceable. |
| Rails analogy and agent-native positioning | Directional framing, useful only while it predicts the constitutional behavior. |
| Universal provider parity, feature-count parity, or every capability attached to Entity | Rejected. |

### Status and evidence boundary

The constitution states what proposals must optimize for. It does not upgrade a capability's maturity.
Source, tests, samples, documentation, packaging, limitations, and compatibility evidence determine
support independently.

## Consequences

### Positive

- Contributors and coding agents can reject misaligned work before designing machinery.
- `Entity<T>` remains first-class without becoming an implementation sink.
- Product positioning can remain ambitious while capability claims stay evidence-bound.
- Startup reporting, machine-readable inspection, and agent behavior converge on one fact model.
- External frameworks become sources and collaborators rather than feature-parity targets.

### Costs

- Existing public prose must be qualified where it presents direction as shipped fact.
- Some attractive module ideas will fail the ownership or meaningful-step test.
- The capability baseline may classify currently advertised surfaces below their implied maturity.

### Neutral

- This decision does not change runtime APIs, packages, provider support, or release timing.
- Current implementation principles remain useful when they do not conflict with the constitution.

## Evidence reviewed

- `README.md` and `docs/getting-started/overview.md` — current product promise and golden path.
- `docs/architecture/principles.md` — existing philosophical and tactical canon.
- `docs/assessment/02-philosophy-dx.md` and `05-strategic-position.md` — prior claim audit and strategy.
- `src/Koan.Core/ServiceCollectionExtensions.cs` and `Hosting/Bootstrap/AppBootstrapper.cs` — actual
  bootstrap and fallback behavior.
- `src/Koan.Core/Hosting/Registry/KoanRegistry.cs` — source-generated registry plus runtime population.
- `src/Koan.Core/Hosting/Runtime/AppRuntime.cs` — best-effort startup and composition reporting.
- `src/Koan.Data.Core/Model/Entity.cs` and Entity extension surfaces — current semantic spine.

## Follow-up

R02 must assign maturity from current evidence and correct remaining overstatement or understatement.
R03 must turn the Entity-first principle into a precise semantic admission and IntelliSense contract.
