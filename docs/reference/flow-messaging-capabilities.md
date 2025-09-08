## Flow & Messaging Capabilities (Inventory)

Contract (summary)
- Inputs: Flow entities (`FlowEntity<T>` / dynamic), adapter-emitted payloads, interceptors, correlation policies.
- Outputs: Transport envelopes, intake records, canonical aggregates/value objects, external ID index entries, diagnostics events.
- Error modes: Unresolved parent key (park), missing aggregation tag (reject), unknown external ID (defer → dead-letter), handler failure (retry/DLQ), serialization case mismatch (key resolution miss).
- Success: Entity payload unmarshalled, metadata applied, queued once, canonical IDs resolved, projection updated.

### Messaging Surface
- Send API: `await entity.Send()` (envelope + queue routing automatic).
- Interceptors: Type/interface registration; returns envelope or `IQueuedMessage`.
- Queue Routing: `FlowQueuedMessage` targets `Sora.Flow.FlowEntity` queue.
- Descriptor Transformers: Legacy `MessagingTransformers.Register(descriptor, fn)` (avoid for Flow entities; keep for generic adaptation).
- Handler Registration: `services.On<string>(...)` for envelope JSON; future: strongly typed orchestrator handlers.

### Flow Entity Pipeline
1. Adapter context established (`FlowContext` AsyncLocal or stack fallback).
2. Entity (or dynamic) intercepted → transport envelope JSON.
3. Message enqueued to dedicated queue.
4. Envelope handler (`FlowMessagingInitializer`) parses and seeds intake record directly.
5. Association + key resolution (external ID / parent linking).
6. Materialization (canonical + lineage).
7. Projection jobs (views) queued as needed.

### Dynamic Flow Entities
- Dictionary extension → `DynamicFlowEntity<T>` conversion.
- Flattened Expando to JSON-path dictionary for payload normalization.

### External ID Correlation
- Populates `identifier.external.{system}`.
- IdentityLink / index enables cross-system parent resolution.
- Refer: ADR DATA-0070.

### Policies & Attributes
- `[FlowAdapter(system, adapter, defaultSource?)]` — identity stamping.
- `[AggregationTag("path")]` — key extraction path.
- `[FlowPolicy(ExternalIdPolicy.*)]` — external ID processing mode.

### Diagnostics & Observability
- Envelope debug logs (length, preview, model, source).
- Parking reasons: `PARENT_NOT_FOUND`, `NO_KEYS`.
- Rejections include evidence tags.

### Edge Cases
| Case | Behavior | Mitigation |
|------|----------|-----------|
| Case mismatch (Serial vs serial) | Key resolution miss | Normalize casing / use OrdinalIgnoreCase lookup |
| Unknown parent at ingest | Park with reason | Retry after parent arrival; admin requeue |
| Missing external ID mapping | Defer with retry | Ensure correlation policies enabled |
| Queue routing fallback provider lacks SendToQueueAsync | Uses generic SendAsync | Implement provider method for efficiency |
| Dynamic entity malformed Expando | Partial flatten | Validate before send |

### Future Hardening (Open Items)
- Strongly-typed orchestrator handler abstraction.
- Backpressure metrics integration.
- Envelope schema version negotiation.
- Adapter health events on queue lag thresholds.

### References
- Guide: ../guides/flow/flow-messaging-architecture.md
- ADR: ../decisions/WEB-0060-flow-messaging-refactor.md
- ADR: ../decisions/DATA-0070-external-id-correlation.md
- Troubleshooting: ../support/troubleshooting/flow-key-resolution.md
