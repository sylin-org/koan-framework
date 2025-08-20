---
id: DATA-0019
slug: DATA-0019-outbox-helper-and-defaults
domain: DATA
status: Accepted
date: 2025-08-17
title: Outbox helper conventions and defaults
---
 
# 0019: Outbox helper conventions and defaults
 

## Context

Multiple outbox implementations (in-memory, Mongo, etc.) need consistent option binding and connection string resolution. We want a default, dev-friendly outbox for implicit CQRS and a clear path for durable providers.

## Decision

- Standardize on a shared helper: `OutboxConfig`.
  - Options binding: `services.BindOutboxOptions<TOptions>("<Adapter>")` binds `Sora:Cqrs:Outbox:<Adapter>`.
  - Connection string precedence: inline > `Sora:Data:Sources:{name}:{provider}:ConnectionString` > `ConnectionStrings:{name}`.
- Default outbox store is `InMemoryOutboxStore` registered by the CQRS module; applications can override by registering another `IOutboxStore` (e.g., Mongo).
- Durable providers (e.g., Mongo) must:
  - Use `OutboxConfig.BindOutboxOptions` for options.
  - Use `OutboxConfig.ResolveConnectionString` for connection strings.
  - Provide leasing and idempotency primitives (min: visibility timeout, attempt counter, unique dedup key).

## Consequences

- Consistent configuration across outbox providers; less copy/paste.
- Out-of-the-box developer experience with a safe in-memory default.
- Clear extension point for production-grade implementations.

## Migration

- Existing outbox packages should refactor to follow `OutboxConfig` for binding and connection string precedence.
- The CQRS initializer registers `InMemoryOutboxStore` by default; adding `AddMongoOutbox()` replaces it.
