# S8.Flow.Api
## Root entity endpoints

Materialized snapshots are persisted as root dynamic entities:

- GET /api/devices — pages `DynamicFlowEntity<Device>` (Id = ReferenceId, Model = nested JSON)
- GET /api/sensors — pages `DynamicFlowEntity<Sensor>`

You can filter by set via `?set=` if needed; default set is the model-qualified base.


IoT monitoring sample using Sora Flow with multi-key aggregation (inventory + serial), namespaced tags, and a live monitor UI (SSE-ready). Two thin adapters run as separate processes/containers and publish over MQ; this API acts as the orchestrator/consumer that persists intake records.

- Keys: device.identifier.inventory, device.identifier.serial
- Adapters: BMS and OEM publishers (separate processes) → MQ → orchestrator (this project)
- Entities (planned): DeviceDoc, SensorDoc, ReadingDoc (Mongo preferred; JSON fallback)
- UI: Lit-based monitor under wwwroot (firehose + per-device dashboard + adapter health)

This is a scaffold. Subsequent commits will add materializers, SSE endpoints, controllers, and UI.
