---
id: ARCH-0108
slug: corrective-host-context-failures
domain: Architecture
status: Accepted
date: 2026-07-14
title: Required terse APIs share one corrective host-context failure
---

# ARCH-0108: Required terse APIs share one corrective host-context failure

## Context

Koan's terse Entity and AI surfaces intentionally hide dependency plumbing. When no application host
was active, however, Data and AI repeated different `InvalidOperationException` strings. A disposed
provider could leak its container exception, and an active host missing a module looked much like no
host at all. Humans and coding agents had to infer both the failed operation and the corrective step.

Optional probes have a different contract: `IsAvailable` and `TryResolve` exist to answer absence
without turning feature discovery into an exception path.

## Decision

Core owns `KoanHostContextException` and `AppHost.GetRequiredService<T>(operation)`:

- the exception derives from `InvalidOperationException` for catch compatibility;
- it names the attempted operation and required service;
- `Failure` distinguishes `MissingHost`, `DisposedHost`, and `MissingService`;
- a disposed provider is retained as the inner `ObjectDisposedException`;
- the message gives the supported host establishment paths: `AddKoan()` with a Koan host,
  `StartKoan()` for non-hosted startup, or an explicit `AppHost.PushScope(provider)`;
- unrelated service-construction exceptions are not caught or relabeled.

Common required Data access, aggregate persistence, transaction creation, AI pipeline use, and AI
adapter selection use this primitive. Explicit-provider overloads and optional `Try*` or availability
probes keep their existing semantics. AI optional discovery treats a disposed provider as unavailable.

`GetRequiredService<T>` is framework infrastructure for terse surfaces and advanced hosting
integrations. Application business code should still use constructor injection; this decision does
not establish a general service-locator pattern.

## Consequences

- Entity-first code remains terse while failures become machine-inspectable and corrective.
- Agents can branch on stable typed facts instead of parsing prose.
- Reviewers can distinguish host lifecycle failure from a missing composed module.
- The contract does not validate every module, connector, metadata helper, or explicit-provider API.
  Those surfaces keep their local contracts until evidence justifies migration.
- Broader composition/error reporting remains R04-05 work; this exception is the narrow host-context
  foundation, not a complete explanation model.

## Evidence

Core contract tests cover all three failure kinds and prove unrelated construction failures pass
through unchanged. Data.Core exercises the common Entity data facade without a host. AI client tests
prove missing and disposed hosts keep optional discovery quiet while required work returns the typed
failure.

## References

- [ARCH-0107 — KoanLog follows canonical host and flow ownership](ARCH-0107-host-scoped-koanlog.md)
- [R04-02 host-scoped runtime](../initiatives/koan-v1/work-items/r04/R04-02-host-scoped-runtime.md)
- [Koan product constitution](../architecture/product-constitution.md)
