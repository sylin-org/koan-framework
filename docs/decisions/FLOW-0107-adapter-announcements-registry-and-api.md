# FLOW-0107 — Adapter announcements, in-memory registry with TTL, and HTTP reporting API

Contract

- Inputs
  - MQ message: AdapterAnnouncement (published by adapters) — system, adapter, instanceId (ULID), version, capabilities[], bus, group, host, pid, startedAt, lastSeenAt, heartbeatSeconds.
  - Optional control request: ControlCommand with verb="announce" to request an immediate reply from an adapter.
- Outputs
  - In-memory registry entry per adapter instance with TTL sweep; continuously updated on heartbeats.
  - HTTP API: GET /api/flow/adapters → { total, by: {"system:adapter": count}, items: AdapterEntry[] }.
  - Optional MQ reply: ControlResponse<AdapterAnnouncement> to a ControlCommand announce.
- Error modes
  - Missing or malformed fields are ignored; message is dropped with a diagnostic log.
  - TTL expiry removes inactive instances; API omits expired records.
  - Clock skew tolerated by treating lastSeenAt=UtcNow if payload is in the future.
- Success criteria
  - Adapters self-register on startup and refresh at a fixed heartbeat interval.
  - Orchestrator can enumerate active adapters by system/adapter pair via HTTP.
  - Optional request/response announce behaves consistently across adapters.

Rationale

We standardize how Flow adapters identify themselves, advertise capabilities, and surface liveness. This powers: basic fleet visibility, simple scaling decisions, and human-friendly diagnostics without imposing external state. Greenfield posture permits a coherent default (auto-on) with opt-outs via options.

Design

- Message contract: AdapterAnnouncement (Sora.Flow.Core)
  - string System — logical system identifier (e.g., "oem", "bms").
  - string Adapter — adapter name within the system (often same as System in simple cases).
  - string InstanceId — ULID (preferred); falls back to container hostname if present.
  - string? Version — adapter version (assembly version by default).
  - string[] Capabilities — e.g., ["seed", "reading"].
  - string Bus, string Group — messaging aliasing info (defaults honored).
  - string Host, int Pid — host-level identity hints.
  - DateTimeOffset StartedAt, LastSeenAt — lifecycle timestamps.
  - int HeartbeatSeconds — adapter’s intended heartbeat cadence.

- Registry and sweeper
  - IAdapterRegistry — abstraction with upsert, remove, query, and All().
  - InMemoryAdapterRegistry — ConcurrentDictionary-backed store with IHostedService sweeper that evicts entries whose LastSeenAt + TtlSeconds <= UtcNow.
  - Options: AdapterRegistryOptions { TtlSeconds=120, HeartbeatSeconds=30, AutoAnnounce=true, ReplyOnCommand=true }.
  - Handler: On<AdapterAnnouncement> upserts entries into the registry.

- Self-announcer
  - AdapterSelfAnnouncer — BackgroundService that discovers [FlowAdapter]-annotated types and periodically publishes AdapterAnnouncement on the default bus/group, gated by options.
  - InstanceId generated via UlidId.New(); fallback to HOSTNAME when ULID not available.

- HTTP API
  - FlowAdaptersController (Sora.Flow.Web): GET /api/flow/adapters returns a summary and the current entries with aggregation by (System, Adapter).

- Optional control response
  - Adapters may implement ControlCommand verb="announce" and reply with ControlResponse<AdapterAnnouncement> for tooling-friendly, request/response flows.

DX and defaults

- Turnkey: builder.Services.AddSora() wires Flow, the registry, and the announcer in container environments. AutoAnnounce is on by default; set to false to disable.
- Configuration keys (examples):
  - Sora:Flow:Adapters:Registry:TtlSeconds
  - Sora:Flow:Adapters:Registry:HeartbeatSeconds
  - Sora:Flow:Adapters:Registry:AutoAnnounce
  - Sora:Flow:Adapters:Registry:ReplyOnCommand

  Example (appsettings.json):
  {
    "Sora": {
      "Flow": {
        "Adapters": {
          "Registry": {
            "TtlSeconds": 180,
            "HeartbeatSeconds": 30,
            "AutoAnnounce": true,
            "ReplyOnCommand": true
          }
        }
      }
    }
  }

Edge cases

- Duplicate instance announcements (same ULID) are consolidated by upsert; LastSeenAt is updated.
- Rapid restarts may advance StartedAt; consumers should not assume monotonicity.
- Multiple buses/groups are supported; Bus/Group surface the active channel for the instance.
- Large fleets: InMemoryAdapterRegistry is intentionally simple; external/stateful registries can be introduced later behind IAdapterRegistry.

Related

- FLOW-0106 — Adapter auto-scan and minimal boot
- WEB-0050 — S8 Flow IoT sample and SSE monitor
- ARCH-0052 — Core IDs and ULID

Status

- Implemented in Sora.Flow.Core and Sora.Flow.Web with sample adapters (OEM, BMS) updated to reply to ControlCommand announce with a ControlResponse payload containing AdapterAnnouncement.
