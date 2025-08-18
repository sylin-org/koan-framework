# 0017: Storage naming conventions (adapter defaults and overrides)

Date: 2025-08-17

## Status
Accepted

## Context
Providers need consistent, overridable conventions for deriving table/collection names from entity types. Explicit mappings must remain authoritative.

## Decision
- Introduce `StorageNamingStyle` (EntityType, FullNamespace), `NameCasing`, and `[StorageNaming]` attribute.
- Centralize default name selection in the framework via `StorageNameRegistry` and `INamingDefaultsProvider` implementations registered by adapters.
- Precedence for name resolution:
  1) `[Storage(Name/Namespace)]` explicit mapping
  2) `[StorageName]` (shortcut)
  3) `[StorageNaming(Style)]` per-entity hint
  4) Adapter defaults via the provider's `INamingDefaultsProvider` (typically bound from options)
- Defaults by adapter:
  - Mongo: FullNamespace, Separator="."
  - Relational (SQLite): EntityType, Separator="_", optional Casing
- Apply conventions in the relational model builder and have adapters consume names from the registry so schema and runtime agree.

Notes
- For nested types, FullNamespace uses the runtime full type name. Namespace separators are replaced by the adapter's separator, while the nested type separator ('+') remains as-is.

## Consequences
- Users get predictable names out-of-the-box with easy overrides.
- Explicit attributes are backward compatible and win over conventions.
- Options allow global tuning without per-entity changes.

## Alternatives considered
- Per-adapter `GetStorageName` functions with duplicated precedence logic. Rejected in favor of centralization for consistency and single-source-of-truth.
