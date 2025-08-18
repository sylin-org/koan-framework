# 0027: Standalone MQ services and naming (Inbox service; Publisher Relay vs Outbox)

- Status: Accepted
- Date: 2025-08-17
- Owners: Sora Messaging

## Context

We added consumer idempotency (Inbox) with optional discovery over MQ and an HTTP client (`HttpInboxStore`). We’re considering standalone services and thin clients for messaging concerns.

True Outbox requires atomic write with the app’s domain database. A remote “Outbox service” can’t provide that without distributed transactions or deep coupling.

## Decision

1) Inbox as a standalone service (recommended)
- Provide a Redis-backed service exposing:
  - GET /v1/inbox/{key}
  - POST /v1/inbox/mark-processed
- Ship a thin client (already: `Sora.Messaging.Inbox.Http`).
- Announce presence over MQ for auto-discovery.

2) Do not build a remote “Outbox service.”
- Keep true Outbox in-process, co-located with the app DB (libraries we already provide).
- Optionally provide a Publisher Relay service for at-least-once publishing (not atomic with DB):
  - POST /v1/publish (accepts message + headers; supports idempotency key; retries; confirms).
  - Document clearly that this is not a transactional outbox.

## Naming conventions

- Service names (Docker/K8s):
  - Inbox service: `sora-service-inbox-redis`
  - Publisher relay: `sora-service-mq-gateway`
- Repo/projects:
  - Service: `Sora.Service.Inbox.Redis` (service runtime/images)
  - Client: `Sora.Messaging.Inbox.Http` (already exists)
  - Optional client: `Sora.Messaging.Publisher.Http`
- HTTP routes:
  - Base: `/v1/inbox` for inbox; `/v1` for relay publish.
- Discovery over MQ:
  - Ping: `sora.discovery.ping.{bus}.{group}`
  - Announce: `sora.discovery.announce.inbox.redis`
  - Payload: `{ kind: "inbox", name: "redis", version: "v1", endpoint: { url } }`
  - Optional: `priority`, `tls`.

## Configuration

- Inbox client endpoint: `Sora:Messaging:Inbox:Endpoint`
- Discovery policy keys per ADR-0026: `Sora:Messaging:Discovery:*`, `Sora:AllowMagicInProduction`.
- Inbox service:
  - `Sora:Inbox:Redis:ConnectionString` (or `ConnectionStrings:InboxRedis`)
  - `Sora:Http:Port` (default 8080), base path `/`.

## Rationale

- Inbox is external by nature (shared idempotency key store). A small HTTP service is appropriate and language-agnostic.
- Outbox must be DB-atomic; keep it in-process. A separate relay can standardize cross-language publishing without promising atomicity.

## Consequences

- We proceed to build `sora-service-inbox-redis` service and finalize discovery/announce.
- We keep improving in-proc outbox libraries. If teams need a remote publish facade, they can use the gateway with clear semantics.

## Follow-ups

- Add discovery client tests validating announce/ping roundtrip and auto-wiring of `HttpInboxStore`. Implemented in `tests/Sora.Mq.RabbitMq.IntegrationTests/DiscoveryE2ETests.cs`.
- Service announce responder implemented in `Sora.Service.Inbox.Redis`: listens to `sora.discovery.ping.{bus}.{group}` and replies with `{ endpoint }`. Configure via `Sora:Messaging:Buses:rabbit:*` and optional `Sora:Messaging:DefaultGroup`.
- Add docs for the Inbox service API and deployment examples (Docker Compose, K8s).
- Evaluate adding selection strategy (priority/name) and caching policy tuning.
