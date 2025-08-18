# ADR 0030: Entity Sets (Logical Storage Routing)

Date: 2025-08-17

Status: Accepted

Context
- We want the same entity type to be stored in different logical "sets" (e.g., root, backup) that route to distinct physical storages/collections/tables.
- Use cases include hot/cold tiers, backups, environment slices, and bulk moves.

Decision
- Introduce a first-class "set" concept that influences storage name resolution and, optionally, connection selection.
- Default mapping strategy: suffix the storage name with `#<set>` (e.g., `S0.Todo#backup`). For the conceptual root set, use no suffix (e.g., `S0.Todo`).
- Provide APIs to operate on a set:
  - Save("set"), Query(predicate, "set"), Remove(predicate/id, "set").
  - Request-level: accept `set` in controller payload to scope operations.
- Extend naming resolution to accept the current set and generate a physical name accordingly; providers may customize mapping.

Implementation Outline
- Add an ambient `DataSetContext` (AsyncLocal<string?>) or explicit parameter across data facade to carry the set.
- Extend `StorageNameRegistry` and adapter repositories to consult the active set when computing physical names.
- Add tests covering Json and Mongo adapters for Save/Query/Remove using non-root sets.

Consequences
- Clear, ergonomic routing with minimal API surface.
- Many sets imply many physical tables/collections with the suffix strategy; acceptable trade-off for isolation. Providers may later support a "column mode" alternative.

References
- `StorageNameRegistry`, `IStorageNameResolver`, Mongo/Sqlite adapters' name computation.
