# S8.Flow — IoT Monitor with Multi-Key Aggregation and SSE

This sample demonstrates Sora Flow aggregating IoT telemetry from multiple adapters into unified device views using namespaced tags and multi-key association, with a live SSE-powered monitor UI.

## Contract

- Inputs: async telemetry events from two adapters (BMS batched; OEM single-reading).
- Keys: device.identifier.inventory, device.identifier.serial.
- Outputs: Projection views (canonical/lineage) and domain JSON (Device/Sensor/Reading) in Mongo (fallback JSON).
- Errors: NO_KEYS, MULTI_OWNER_COLLISION, KEY_OWNER_MISMATCH are surfaced as rejections and on SSE.

## How it’s wired

- FlowOptions.AggregationTags = [device.identifier.inventory, device.identifier.serial]
- Namespaced tags (strings) come from a shared constants project and map 1:1 to nested JSON in Mongo.
- Two hosted adapters emit fake but consistent data at different rates and shapes.
- Materializers upsert Device/Sensor docs and append bounded Reading docs.
- SSE endpoints stream device snapshots, sensor readings, rejections, and adapter health.

## Developer surface

- Controllers only (no inline endpoints).
- First-class entity statics for data access (All/Query/Page/Save/Delete) with set scoping.
- No magic values: keys/units/codes/routes centralized.

## Partitioning and canonical bindings

- Aggregates (Device, Sensor) are kept separate from value objects (SensorReading).
- Adapters emit OEM/source IDs; Flow resolves canonical IDs via `KeyIndex<T>` using binding hints.
- Readings are persisted with Sensor/Device canonical IDs and lineage; queries target canonical ids; retention via TTL.
- See reference: [Bindings and canonical IDs](../reference/flow-bindings-and-canonical-ids.md).

## References

- WEB-0050 — S8 Flow IoT sample and SSE monitor (ADR)
- WEB-0035 — EntityController transformers
- DATA-0061 — All/Query/Stream/Pager patterns
- AI-0002 — API contracts and SSE format