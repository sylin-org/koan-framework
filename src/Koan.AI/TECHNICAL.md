---
uid: reference.modules.Koan.ai
title: Koan.AI - Technical Reference
description: AI baseline, registry, routing, and policies.
since: 0.2.x
packages: [Sylin.Koan.AI]
source: src/Koan.AI/
---

## Contract

- AI adapters, registry, routing and policy hooks.
- `IAiPipeline` is a singleton in the composed host. `Client` resolves the active host at operation
  time and does not retain a process-global provider or pipeline resolver.
- `Client.IsAvailable` and `Client.TryResolve()` return absence for a missing or disposed host.
- Required pipeline operations and adapter selection use `KoanHostContextException` to distinguish a
  missing host, disposed host, and missing composed service. Adapter capability absence remains its
  own operation-specific error after the registry is resolved.

## References

- AI-0001..0010 decisions: `/docs/decisions/`
