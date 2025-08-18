# Domain-Driven Design (DDD) in Sora

Sora embraces DDD as a practical toolbox for building modular, evolvable systems. This section aligns Sora’s building blocks (Core, Data, Messaging, Web) with DDD concepts so you can apply a ubiquitous language, clear tactical patterns, and sensible boundaries in your apps.

Use these docs with the rest of Sora docs and ADRs. Each page maps DDD ideas to concrete Sora APIs and samples.

Contents
- 01-ubiquitous-language.md — Ubiquitous language and term mapping
- 02-bounded-contexts-and-modules.md — Bounded contexts and modular composition
- 03-tactical-design.md — Aggregates, repositories, services, invariants
- 04-cqrs-and-eventing-in-sora.md — CQRS, outbox/inbox, messaging
- 05-sample-walkthrough.md — Mapping S2/S3 samples to DDD
- 06-testing.md — Testing aggregates and adapters
- 07-anti-corruption-layer.md — ACL patterns at system boundaries
- 08-cross-cutting-and-observability.md — Tracing/metrics, headers, policies

Related
- ../12-cqrs-for-humans.md — Intro to CQRS in Sora
- ../03-core-contracts.md, ../04-adapter-authoring-guide.md — Core contracts and adapters
- ../decisions/0032-paging-pushdown-and-in-memory-fallback.md
- ../decisions/0033-opentelemetry-integration.md

---
Applied DDD should feel natural in Sora: model the domain first, keep boundaries explicit, and let infrastructure (adapters, messaging, web) serve the model.

## Terms in plain language
- Domain: the problem space you're solving (e.g., orders, billing).
- Model: code that represents the domain (entities, value objects, services).
- Bounded Context: a clear semantic boundary with its own model and language.
- Aggregate: a small cluster of objects changed together; one entry point (root).
- Repository: an interface to load/save aggregates without leaking storage details.
- CQRS: separate paths for commands (writes) and queries (reads).
- Event (Domain/Integration): a fact that something happened; used to notify others.
- Outbox/Inbox: reliability patterns to publish/consume events safely.
- Eventual Consistency: different parts agree over time, not instantly.
- ACL (Anti-Corruption Layer): a translator to keep external models from polluting yours.
- Observability: tracing/metrics/logs to understand behavior in production.
