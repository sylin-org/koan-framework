# 0027: Standalone MQ services and naming (Inbox service; Publisher Relay vs Outbox)

- Status: Accepted
- Date: 2025-08-17
- Owners: Koan Messaging

## Context

We added consumer idempotency (Inbox) with optional discovery over MQ and an HTTP client (`HttpInboxStore`). We’re considering standalone services and thin clients for messaging concerns.

True Outbox requires atomic write with the app’s domain database. A remote “Outbox service” can’t provide that without distributed transactions or deep coupling.

## Decision

1) Inbox as a standalone service (recommended)
- Provide a Redis-backed service exposing:
  - GET /v1/inbox/{key}
  - POST /v1/inbox/mark-processed
- Ship a thin client (already: `Koan.Messaging.Inbox.Connector.Http`).
- Announce presence over MQ for auto-discovery.

2) Do not build a remote “Outbox service.”
- Keep true Outbox in-process, co-located with the app DB (libraries we already provide).
- Optionally provide a Publisher Relay service for at-least-once publishing (not atomic with DB):
  - POST /v1/publish (accepts message + headers; supports idempotency key; retries; confirms).
  - Document clearly that this is not a transactional outbox.

## Naming conventions

- Service names (Docker/K8s):
  - Inbox service: `Koan-service-inbox-redis`
  - Publisher relay: `Koan-service-mq-gateway`
- Repo/projects:
  - Service: `Koan.Service.Inbox.Connector.Redis` (service runtime/images)
  - Client: `Koan.Messaging.Inbox.Connector.Http` (already exists)
  - Optional client: `Koan.Messaging.Publisher.Http`
- HTTP routes:
  - Base: `/v1/inbox` for inbox; `/v1` for relay publish.
- Discovery over MQ:
  - Ping: `Koan.discovery.ping.{bus}.{group}`
  - Announce: `Koan.discovery.announce.inbox.redis`
  - Payload: `{ kind: "inbox", name: "redis", version: "v1", endpoint: { url } }`
  - Optional: `priority`, `tls`.

## Configuration

- Inbox client endpoint: `Koan:Messaging:Inbox:Endpoint`
- Discovery policy keys per ADR-0026: `Koan:Messaging:Discovery:*`, `Koan:AllowMagicInProduction`.
- Inbox service:
  - `Koan:Inbox:Redis:ConnectionString` (or `ConnectionStrings:InboxRedis`)
  - `Koan:Http:Port` (default 8080), base path `/`.

## Rationale

- Inbox is external by nature (shared idempotency key store). A small HTTP service is appropriate and language-agnostic.
- Outbox must be DB-atomic; keep it in-process. A separate relay can standardize cross-language publishing without promising atomicity.

## Consequences

- We proceed to build `Koan-service-inbox-redis` service and finalize discovery/announce.
- We keep improving in-proc outbox libraries. If teams need a remote publish facade, they can use the gateway with clear semantics.

## Follow-ups

- Add discovery client tests validating announce/ping roundtrip and auto-wiring of `HttpInboxStore`. Implemented in `tests/Koan.Mq.RabbitMq.IntegrationTests/DiscoveryE2ETests.cs`.
- Service announce responder implemented in `Koan.Service.Inbox.Connector.Redis`: listens to `Koan.discovery.ping.{bus}.{group}` and replies with `{ endpoint }`. Configure via `Koan:Messaging:Buses:rabbit:*` and optional `Koan:Messaging:DefaultGroup`.
- Add docs for the Inbox service API and deployment examples (Docker Compose, K8s).
- Evaluate adding selection strategy (priority/name) and caching policy tuning.

