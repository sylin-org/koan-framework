# 0018: Centralized storage naming registry and developer ergonomics

Date: 2025-08-17

Status: Accepted

## Context

We previously derived table/collection names per adapter, duplicating precedence logic and making app-level overrides cumbersome. We also needed a way to cache rendered names and ensure schema builders and adapters used the same decisions.

## Decision

- Centralize naming in the framework via `StorageNameRegistry`, which computes and caches names in `AggregateBags`.
- Provide DI extension points:
  - `IStorageNameResolver` (default implementation registered) for the core algorithm and app-level overrides.
  - `INamingDefaultsProvider` (per provider) to surface adapter defaults from options (`MongoOptions`, `SqliteOptions`, etc.).
- Make defaults provider optional:
  - If none is registered for a provider, fall back to `NamingFallbackOptions` (configurable via `Sora:Data:Naming:{Style,Separator,Casing}`) or a safe built-in.
- Add one-liner overrides for DX:
  - `services.OverrideStorageNaming((Type type, Convention conv) => string? name)` to globally override without custom classes.

Adapters (Mongo, SQLite) now consume names via `StorageNameRegistry` and no longer implement per-adapter `GetStorageName` functions.

## Precedence

1) `[Storage(Name/Namespace)]` explicit mapping
2) `[StorageName]` shortcut
3) `[StorageNaming(Style)]` per-entity hint
4) Provider defaults (via `INamingDefaultsProvider` or inferred from options)
5) Global fallback (`NamingFallbackOptions`) when no provider defaults exist

Repository-level overrides are still honored when a repository implements `IStorageNameResolver`.

## Consequences

- Consistency: One algorithm and precedence enforced across adapters and schema tooling.
- Simplicity: Apps can set org-wide rules with a single delegate or fallback options; no subclassing or forking adapters.
- Testability: Policies are injected; no global static state required.
- Performance: Names are cached in per-entity bags.

## Alternatives considered

- Static singleton policy: simpler wiring but poor test isolation and multi-provider composition.
- Per-adapter naming methods: duplication and drift; harder to override uniformly.

## Migration

- Adapters should register an `INamingDefaultsProvider` (or rely on options/fallback) and read names via `StorageNameRegistry.GetOrCompute<TEntity,TKey>(sp)`.
- Existing per-adapter naming helpers should be deprecated and redirected to the central resolver where possible.
