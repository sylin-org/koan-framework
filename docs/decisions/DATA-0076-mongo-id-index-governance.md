# DATA-0076: Mongo `_id` index governance and repository defaults

**Contract**

- **Inputs:** `MongoRepository<TEntity,TKey>` collection bootstrap, `IndexMetadata` definitions emitted from entity attributes, Koan schema guard orchestration (DATA-0075).
- **Outputs:** Mongo collections rely on the server-managed `_id` primary index, repository ensure flows emit successful outcomes without command failures, downstream adapters keep custom indexes aligned with metadata.
- **Error modes:** Deployment targets that override the default `_id` shape, legacy dumps with conflicting `_id` options, unit tests that mock drivers without honoring implicit indexes.
- **Success criteria:** No `createIndexes` commands are issued for `_id`, ensure loops stop logging `unique` validation errors, custom metadata indexes continue to be provisioned deterministically, schema guard warm-ups run without retries.

Status: Accepted

## Context

Koan's Mongo adapter historically baked an explicit `CreateIndexModel` for the `_id` field into every collection ensure. MongoDB already materializes a unique ascending index on `_id` for all collections. When `CreateMany` replays the redundant specification with `unique: true`, the server rejects the request (`The field 'unique' is not valid for an _id index specification`), causing boot-time debug noise and delayed readiness for samples like `S5.Recs`.

Because collection ensure now runs under the shared schema guard (DATA-0075), these failures bubble through the guard retry path and obscure real provisioning issues. The repository should trust Mongo's intrinsic constraints and only emit indexes declared via metadata.

## Decision

1. Remove the hard-coded `_id` index creation from `MongoRepository.BuildIndexModels()`.
2. Allow MongoDB to provide the default `_id` unique index; Koan only provisions metadata-driven secondary indexes.
3. Leave cache keys, guard coordination, and remaining index logic untouched to avoid race regressions.
4. Document the policy so future contributors do not reintroduce `_id` shims or conflicting options.

## Implementation

- Update `MongoRepository.BuildIndexModels()` to return models exclusively derived from `IndexMetadata`.
- Retain the existing index cache since the `_id` model removal requires no additional guards.
- Validate via `dotnet build` + Mongo-backed sample boot to ensure the ensure path completes silently.

## Consequences

- Repository startup no longer emits command errors when ensuring indexes for collections lacking additional metadata.
- Mongo deployments that intentionally customize `_id` (e.g., hashed) remain compatible because Koan no longer forces a competing definition.
- Consumers must continue to rely on Koan's index metadata surfaces for additional constraints; `_id` uniqueness is implicitly guaranteed by MongoDB.

## Follow-up

1. Audit other adapters for redundant primary index declarations to maintain parity with provider defaults.
2. Extend diagnostics to surface when custom metadata attempts to redefine `_id`, ensuring contributors receive early feedback.