# Composable stack previews

Use these cards to turn a desired experience into a few semantic Koan pieces. They are starting compositions, not templates. Verify each selected piece with [capabilities.md](capabilities.md), then add only what the first useful journey needs.

## Read the working version first

Most cards have a counterpart that runs. Prefer showing a developer the running shape over describing it — these are compiled applications, so they cannot drift from the framework:

| Card | Runs today |
|---|---|
| Durable local Entity API · Agent-operable application | [FirstUse](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/FirstUse/README.md) — one Entity becomes persisted data, an HTTP API, and a governed agent tool |
| Zero-service prototype | [LocalChecklist](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/fundamentals/LocalChecklist/README.md) |
| Reliable background workflow · Model-powered operation | [GoldenJourney](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/GoldenJourney/README.md) — a rule, durable background work, then a bounded agent recommendation |
| Semantic search | [GardenCoop: Local Discovery](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/journeys/GardenCoop/02-LocalDiscovery/README.md) — no Docker, API key, or vector server |
| Entity-owned media · Tenant-isolated classified service | [SnapVault](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/applications/SnapVault/README.md) |
| Trusted-record pipeline | [CustomerCanon](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/applications/CustomerCanon/README.md) |
| Networked data service | [DevPortal](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/applications/DevPortal/README.md) — publishing to a second named source |
| Observable service | [OrderIntake](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/applications/OrderIntake/README.md) — a batch, verification, cleanup, and an honest receipt |
| Relationships across Entity, set, and stream | [TaskGraph](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/fundamentals/TaskGraph/README.md) |

Governed Web application, Evented integration, and Cache-backed experience have no single counterpart; compose them from the cards below. The [full catalog](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/README.md) lists every runnable example.

## Durable local Entity API

**Story:** “Store my things and expose them over HTTP.”

- **Required now:** an Entity, SQLite, Web, and an `EntityController<T>`.
- **Easy later:** auth, OpenAPI, SSE, another adapter, or MCP.
- **Preserved:** Entity calls and public resource semantics.
- **Prove:** create/read/query, restart persistence, selected SQLite composition, and invalid storage failure.

## Zero-service prototype

**Story:** “Let me explore the domain before choosing infrastructure.”

- **Required now:** an Entity plus JSON for file-backed state or InMemory for deliberately disposable state.
- **Easy later:** SQLite or a networked adapter, Web, tests, and operations.
- **Preserved:** the Entity vocabulary and application rules.
- **Prove:** the intended durability boundary, bounded data use, and a clear correction when the prototype guarantee is exceeded.

## Networked data service

**Story:** “Use MongoDB, PostgreSQL, SQL Server, Redis, Couchbase, or CockroachDB.”

- **Required now:** an Entity, the chosen adapter, and explicit connection/source intent.
- **Easy later:** named routes, a second adapter, cutover, cache, or tenancy.
- **Preserved:** routes, payloads, database naming, and provider-neutral Entity calls.
- **Prove:** provider-specific write/read, visible selection, supported query shape, and corrective connection failure. Plan existing-data movement separately.

## Governed Web application

**Story:** “Expose useful APIs without duplicating business rules.”

- **Required now:** Entities, Web, `EntityController<T>`, and operation-level authorization.
- **Easy later:** OpenAPI, SSE, shaping, social projections, or MCP.
- **Preserved:** one lifecycle/access rule behind every projection.
- **Prove:** allowed and denied HTTP journeys, bounded paging, projection metadata, and shared Entity behavior.

## Tenant-isolated classified service

**Story:** “Each customer sees only its own protected work.”

- **Required now:** identity, access policy, Tenancy, tenant-owned Entity boundaries, and classification where fields need protection.
- **Easy later:** tenant-scoped Jobs, storage, events, vectors, or administration.
- **Preserved:** one tenant cannot infer another through counts, errors, streams, resources, or generated projections.
- **Prove:** anonymous, allowed, forbidden, cross-tenant, and protected-field journeys through every enabled surface.

## Reliable background workflow

**Story:** “Return quickly, finish reliably, and show progress.”

- **Required now:** an Entity receipt, Jobs, an idempotent handler, and progress/failure semantics.
- **Easy later:** scheduling, Communication, SSE progress, or MCP operations.
- **Preserved:** accepting work and completing work remain distinct contracts.
- **Prove:** retry, duplicate submission, cancellation, failure visibility, and restart behavior where promised.

## Evented integration

**Story:** “Tell another system when a business event happens.”

- **Required now:** an owning Entity occurrence/snapshot, Communication, and an explicit delivery contract.
- **Easy later:** a broker transport, Jobs orchestration, settlement telemetry, or MCP resources.
- **Preserved:** event meaning stays independent of transport.
- **Prove:** acceptance versus settlement, duplicate/retry behavior, cancellation, and transport outage.

## Cache-backed experience

**Story:** “Make repeated reads fast without changing truth.”

- **Required now:** a source-of-truth Entity, cache policy, and an adapter suited to local or shared use.
- **Easy later:** distributed invalidation, refresh workflows, or telemetry.
- **Preserved:** cache remains an optimization; authoritative behavior and tenant boundaries do not move into it.
- **Prove:** hit/miss, invalidation, stale/degraded policy, isolation, and source fallback only where explicitly allowed.

## Entity-owned media

**Story:** “Upload once and serve useful renditions.”

- **Required now:** Entity-owned Storage, Media, a named recipe, processing bounds, and governed delivery.
- **Easy later:** durable ingest Jobs, SSE, tenancy, or a remote storage connector.
- **Preserved:** original ownership, derivative identity, access policy, and retention.
- **Prove:** upload, derive, retrieve, deny unauthorized access, and expose processing failure.

## Model-powered operation

**Story:** “Use AI for this named business task.”

- **Required now:** an inspectable prompt/operation, AI runtime, one provider/model route, and explicit failure policy.
- **Easy later:** another provider/category, durable prompts, evaluation, HTTP projection, or Jobs.
- **Preserved:** application semantics and sensitive-data policy when the model changes.
- **Prove:** input/output contract, selected provider, unavailable-provider correction, latency/cancellation, and sensitive-data handling.

## Semantic search

**Story:** “Find similar Entities by meaning.”

- **Required now:** source Entity, embedding ownership, embedding provider, Vector runtime/provider, and bounded results.
- **Easy later:** hybrid filters, background re-embedding, an external index, or verified vector movement.
- **Preserved:** source-of-truth data plus embedding and index provenance.
- **Prove:** known-neighbor relevance, dimensions/version, filters and paging actually supported, empty/degraded behavior, and bounded work.

## Agent-operable application

**Story:** “Let an agent understand and safely operate my app.”

- **Required now:** existing governed operations, MCP tools/resources, self-description, and a deliberate transport boundary.
- **Easy later:** remote transport, Explorer, operational resources, or more bounded tools.
- **Preserved:** HTTP and MCP reach the same authorization, tenant, lifecycle, and business rules.
- **Prove:** discovery, allowed read/action, denied action, resource semantics, caller scope, transport trust, and audit evidence.

## Trusted-record pipeline

**Story:** “Turn inconsistent arrivals into one trusted record.”

- **Required now:** Canon, source identity, deterministic matching, ambiguity policy, provenance, and a trusted Entity.
- **Easy later:** review workflow, audit resources, tenant partitions, Communication, or MCP.
- **Preserved:** raw arrivals and explainable reconciliation.
- **Prove:** match, no-match, ambiguity, replay, review/commit, and commit failure.

## Observable service

**Story:** “Make the real stack understandable and operable.”

- **Required now:** behavior/composition/correction tests, facts, health, diagnostics, telemetry, explicit external topology, and secret redaction.
- **Easy later:** capability-specific signals that follow actual reliability needs.
- **Preserved:** no hidden adapter, optional dependency, test identity, or fallback becomes the apparent production path.
- **Prove:** primary journey, selected providers, readiness failure, negative access paths, redaction, and the important external dependency failures.
