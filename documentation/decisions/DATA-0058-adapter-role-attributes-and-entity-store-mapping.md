# DATA-0058 - Adapter role attributes and explicit entity store mapping

Status: Accepted

Date: 2025-08-21

## Context

Koan supports multiple data adapters in a single application (document stores, relational, vector DBs, etc.). In multi-adapter scenarios, implicit routing can lead to ambiguity (for example, entities inadvertently going to the wrong adapter or vector features being disabled). The S5.Recs sample highlighted this risk: documents were imported but vectors were not created due to adapter disambiguation and endpoint defaults.

We already use `[Storage(Name=…)]` for logical set naming and `[DataAdapter("…")]` appears in samples. We need a first-class, role-aware way to declare where an entity’s data lives per modality (documents vs. vectors) and to define resolution precedence in `DataService`.

## Decision

Introduce role-specific attributes to explicitly map entities to adapters:

- `[SourceAdapter("<provider>")]` - declares the primary/document adapter for the entity (for example, `mongo`, `postgres`, `sqlite`).
- `[VectorAdapter("<provider>")]` - declares the vector adapter for the entity (for example, `weaviate`, `redis`, `pgvector`).

These can be applied independently on the same entity type. Continue to use `[Storage(Name=…, Namespace?=…)]` for the logical set/class name. The attributes don’t carry tunables; adapter-specific options remain in typed options/config.

Resolution precedence in `DataService` and repositories:

1. Per-operation override (explicit repository/adapter requested programmatically)
2. Entity-level attribute for the relevant role
3. Application/module default adapter for that role
4. If no adapter is resolvable, fail fast with a clear diagnostic (do not silently pick an arbitrary adapter)

This keeps routing predictable and self-documenting at the entity declaration.

## Consequences

- Clear, role-aware mapping removes ambiguity in multi-adapter apps.
- Entities can participate in both doc and vector workflows without naming conflicts.
- No magic defaults: when unclear, resolution fails early with actionable errors.
- Samples can teach best practices by annotating entities with both roles.

## Alternatives considered

- Single attribute with a Role parameter (e.g., `[DataAdapter(Role=Doc,…)]`), rejected for readability. Separate attributes are clearer and align with separation of concern.
- Convention-only mapping (namespace/prefix), rejected due to brittleness across modules.

## Migration notes

- Existing `[DataAdapter("mongo")]` continues to work for document routing. To opt into vectors, add `[VectorAdapter("weaviate")]` (or appropriate provider) to the entity.
- No storage name changes are required; `[Storage(Name=…)]` continues to govern set/class naming.

## Example

```csharp
[SourceAdapter("mongo")]
[VectorAdapter("weaviate")]
[Storage(Name = "Anime")] // logical set/class
public sealed class AnimeDoc : IEntity<string>
{
    public string Id { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string[] Genres { get; init; } = Array.Empty<string>();
    // … other fields …
}
```

## Testability

Add unit tests around `DataService` resolution to assert:

- Attribute-driven routing for doc and vector roles
- Per-operation override precedence
- Clear exception when no adapter is resolvable

## Operational guidance

- Keep adapter endpoints and credentials in typed options bound from configuration, not in attributes.
- Prefer failing fast on misconfiguration; surface actionable diagnostics in logs.
