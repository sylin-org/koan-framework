# S8.Flow.Api
## Root entity endpoints

Materialized snapshots are persisted as root dynamic entities:

- GET /api/devices — pages `DynamicFlowEntity<Device>` (Id = ULID, CanonicalId = business key, Model = nested JSON)
- GET /api/sensors — pages `DynamicFlowEntity<Sensor>`

You can filter by set via `?set=` if needed; default set is the model-qualified base.


IoT monitoring sample using Koan Flow with multi-key aggregation (inventory + serial), namespaced tags, and a live monitor UI (SSE-ready). Two thin adapters run as separate processes/containers and publish over MQ; this API acts as the orchestrator/consumer that persists intake records.

- Keys: device.identifier.inventory, device.identifier.serial
- Adapters: BMS and OEM publishers (separate processes) → MQ → orchestrator (this project)
- Entities (planned): DeviceDoc, SensorDoc, ReadingDoc (Mongo preferred; JSON fallback)
- UI: Lit-based monitor under wwwroot (firehose + per-device dashboard + adapter health)

This is a scaffold. Subsequent commits will add materializers, SSE endpoints, controllers, and UI.

Notes

- Ingestion uses the normalized sender (`IFlowSender`) with server-side stamping and reserved key prefixes (plain bag):
	- `identifier.external.*` for adapter-native IDs
	- `reference.*` for parent references (e.g., `reference.device`)
	- `model.*` for direct model fields
	Clients don’t stamp identity; the API infers it from the envelope/host. See ADR FLOW-0105 and docs/guides/data/all-query-streaming-and-pager.md for conventions.

	Additional tips

	- Use `FlowEntitySendExtensions.Send()` for entities and `FlowValueObjectSendExtensions.Send()` for value objects with the messaging-first pattern.
	- For adapter commands, define a small VO (e.g., `ControlCommand`) and handle it in the adapter host to react to verbs.
