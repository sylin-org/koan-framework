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
  - `ReferenceItem`: Version++, RequiresProjection=true
  - `ProjectionTask` enqueued
  - `RejectionReport` on policy violations

## Association rules (strict)
- No configured tags or no values -> reject `NO_KEYS`
- Multiple existing owners for candidate keys -> reject `MULTI_OWNER_COLLISION`
- Single existing owner -> use its ReferenceId
- No owners -> onboard with `CorrelationId` if present, else first candidate value
- For each key: if an existing owner differs from chosen ReferenceId -> reject `KEY_OWNER_MISMATCH`

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

## Related decisions and guides
- See `/docs/engineering/index.md`, `/docs/architecture/principles.md`
- Data access pagination and streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web payload shaping: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
