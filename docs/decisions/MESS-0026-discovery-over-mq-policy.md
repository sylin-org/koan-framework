---
id: MESS-0026
slug: MESS-0026-discovery-over-mq-policy
domain: MESS
status: Accepted
title: Optional discovery-over-MQ (ping/announce) policy and gating
---
 
# 0026 — Optional discovery-over-MQ (ping/announce) policy and gating

Context
- In compose/dev, auto-discovering ancillary services (e.g., Inbox microservice) reduces config and improves DX.
- In production, we want explicit configuration and fewer moving parts.

Decision
- Provide an optional MQ-based ping/announce mechanism for dev/compose. Off by default in Production, On in other environments. Overridden by the global Magic flag (Sora:AllowMagicInProduction). Discovery runs only when no explicit inbox endpoint is configured.

Mechanics
- Aliases
  - sora.discovery.ping.{bus}.{group}
  - sora.discovery.announce.{bus}.{group}
- Payload
  - kind: "inbox", name, version
  - endpoint: { scheme: http|https, url, auth: none|bearer }
  - leaseDefaults: { seconds }, retentionDays
  - priority: int
  - correlationId (mirrors request)
- Flow
  - Client publishes ping with correlationId and reply-to; short TTL (2–5s)
  - Inboxes reply with announce to reply-to or the announce topic, include correlationId
  - Client picks a candidate deterministically (priority/name), caches for N minutes, and logs selection

Gating and precedence
- If Sora:Messaging:Inbox:Endpoint (or Provider) is set → skip discovery
- Else Discovery:Enabled follows env defaults: On in non-Production, Off in Production

Implementation details (as shipped)
- Ping routing key: `sora.discovery.ping.{bus}.{group}`. The client publishes with `reply-to` (auto queue) and `correlationId`.
- Announce: services SHOULD reply to `reply-to` with the same `correlationId` and payload containing an endpoint URL, e.g. `{ "endpoint": "http://host:port" }`.
- The discovery client binds a temp queue to `sora.discovery.announce.#` to be compatible with announce broadcasts, but primarily listens using `reply-to` for direct responses.
- Timeouts and selection:
  - `Sora:Messaging:Discovery:TimeoutSeconds` (default 3) bounds the discovery round.
  - `Sora:Messaging:Discovery:SelectionWaitMs` (default 150) waits briefly after the first announce to collect multiple candidates before selecting.
  - `Sora:Messaging:Discovery:CacheMinutes` (default 5) caches a discovered endpoint to avoid repeated pings.
- Precedence: explicit endpoint always wins; policy gating controls whether discovery runs at all.
- Sora:AllowMagicInProduction=true overrides env gating

Operational safety
- Namespaced routing by bus/group
- No secrets in payloads; only auth hints
- Bounded retries and single warning when no responses

Consequences
- Better zero-config DX in compose/k8s labs
- Predictable, secure defaults for production
- Clear precedence and logging for operator clarity
