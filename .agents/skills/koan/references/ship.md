# Runtime readiness

Use this reference when the application must be safe to deploy or operate.

Begin with the actual semantic stack: selected providers/routes, public doorways, state, external dependencies, identity and tenant boundaries, background work, and operational evidence. Apply checks only to capabilities the application uses.

## Contracts

- Keep public routes, payloads, resources, tool schemas, and database/storage naming intentional.
- Make important provider routes explicit.
- Bound paging, streams, Jobs, media work, AI calls, retries, and cancellation.
- Give state movement a verified recovery boundary.

## Security and privacy

- Keep production identity and authorization explicit; exclude development/test identities.
- Apply tenant constraints across HTTP, MCP, Jobs, events, cache, storage, media, AI, and vectors.
- Keep secrets outside source and redact them from facts, logs, and lock artifacts.
- Make remote MCP and external AI/vendor access deliberate network, grant, audit, and data-egress boundaries.

## Reliability and operations

- Make required dependencies affect readiness; make optional degradation explicit.
- Keep retryable work idempotent and expose durable status where promised.
- Distinguish Communication acceptance from settlement.
- Never hide a topology or configuration failure with provider fallback.
- Assign provisioning, backup, recovery, and failover to real owners.
- Make facts, health, logs, metrics, and traces identify the owning operation and selected route.

## Evidence

Run the primary journey against a representative configuration, assert named providers, exercise negative identity/tenant and invalid dependency paths, and state any environmental boundary not run. Report ready, ready with explicit boundaries, or not ready, followed by the few material risks and next corrective action.
