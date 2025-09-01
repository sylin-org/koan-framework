# Sora.Flow.Core — Technical Reference

This project houses the provider-neutral Flow core: options, entities, DI wiring, and background workers for association (keying) and projection.

## Contracts (inputs/outputs)
- Input: Records in `Sets.Intake` (IDictionary<string, object> StagePayload)
- Options: `FlowOptions`
  - `AggregationTags: string[]` — ordered candidate tags for aggregation keys
  - TTLs: Intake/Standardized/Keyed/ProjectionTask/RejectionReport
  - BatchSize, PurgeEnabled, PurgeInterval, DefaultViewName
- Output:
  - Records moved to `Sets.Keyed` with `CorrelationId = ReferenceId`
  - `KeyIndex`: AggregationKey -> ReferenceId
  - `IdentityLink<TModel>`: (system|adapter|externalId) -> ReferenceId (provisional allowed)
  - `ReferenceItem`: Version++, RequiresProjection=true
  - `ProjectionTask` enqueued
  - `RejectionReport` on policy violations

## Association rules (strict)
- No configured tags or no values -> reject `NO_KEYS`
- Multiple existing owners for candidate keys -> reject `MULTI_OWNER_COLLISION`
- Single existing owner -> use its ReferenceId
- No owners -> if envelope has `system` + `adapter` and at least one discovered external-id key, try identity map:
  - If an `IdentityLink` exists -> use its `ReferenceId`.
  - If missing -> issue a new ULID and create a provisional `IdentityLink` pointing to that ULID; return the ULID as `ReferenceId`.
  - If envelope keys aren’t present -> onboard with `CorrelationId` if present, else first candidate tag value.
- For each key: if an existing owner differs from chosen ReferenceId -> reject `KEY_OWNER_MISMATCH`

Identity map
- Entity: `IdentityLink<TModel>` with fields (Id, System, Adapter, ExternalId, ReferenceId, Provisional, CreatedAt, ExpiresAt)
- Id format: `"{system}|{adapter}|{externalId}"` for O(1) lookups across providers
- External-id keys are discovered from `[EntityLink(typeof(Model), LinkKind.ExternalId)]` properties across loaded assemblies (no hardcoded names)
- Resolution in association first tries IdentityLink; if missing, issues a canonical ULID and creates a provisional link to that ULID, then returns the ULID as ReferenceId
- Provisional links get a soft TTL (ExpiresAt ~ now + 2 days) and are purged by the Flow purge worker when expired

Envelope keys
- Common envelope keys used in intake payloads: `system`, `adapter` (Constants.Envelope). External-id fields must be present using the exact property names marked with `[EntityLink(..., ExternalId)]` for the target model.

## Projection reducer
- Canonical view: tag -> unique values[]
- Lineage view: tag -> value -> sourceIds[]
- Clears `ReferenceItem.RequiresProjection` after materialization

## Developer samples
- Page intake and keyed via entity statics: `Record.Page(...)`, `Record.Query(...)`
- Get and Save first-class statics: `ReferenceItem.Get(id)`, `ri.Save()`, `new ProjectionTask{...}.Save()`

## Options
Example appsettings.json section:

```
{
  "Sora": {
    "Flow": {
      "AggregationTags": ["email", "phone", "externalId"],
      "BatchSize": 64,
      "PurgeEnabled": true,
      "PurgeInterval": "00:05:00"
    }
  }
}
```

## Edge cases
- Empty payload or non-dictionary `StagePayload`
- Very large batches -> adjust `BatchSize` and purge intervals
- Duplicate keys from different sources -> lineage captures sources; collisions rejected as above
 - Missing envelope fields -> IdentityLink resolution is skipped

## Related decisions and guides
- See `/docs/engineering/index.md`, `/docs/architecture/principles.md`
- Data access pagination and streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web payload shaping: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
