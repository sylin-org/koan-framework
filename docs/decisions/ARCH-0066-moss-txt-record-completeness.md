---
id: ARCH-0066
slug: moss-txt-record-completeness
domain: ARCH
status: Proposed
date: 2025-02-09
---

# ARCH-0066: Moss TXT record completeness

Date: 2025-02-09

Status: Proposed

## Context

Moss registers itself as `_moss._tcp` via mDNS with TXT metadata. The
discovery spec (`zen-garden/docs/specs/discovery.md`) defines the
following TXT schema:

```
TXT: stone_name=stone-01
     stone_id=<uuid>
     version=0.1.0
     api_port=7185
     health=healthy
     mac=AA:BB:CC:DD:EE:FF
```

In practice, live Moss instances only advertise a subset:

```
TXT: stone_name=stone-01
     stone_id=<uuid>
     api_port=7185
     mac=AA:BB:CC:DD:EE:FF
```

Two fields defined in the spec are missing from live data:

- **`version`** — the Moss version string (e.g. `0.1.0`)
- **`health`** — the Stone's self-reported health status (e.g.
  `healthy`, `degraded`)

DATA-0091 (Koi as authoritative topology handler) defines
`DiscoveredStone` with `MossVersion` and `Health` properties mapped
from these TXT fields. The `KoiHandler` will pass them through if
present — Koi is transparent to TXT records — but currently they're
always null because Moss doesn't advertise them.

## Decision

Moss should advertise `version` and `health` in its `_moss._tcp` TXT
record, matching the discovery spec.

### `version`

Emit the Moss assembly version (or a build-stamped version string) at
registration time. This is static for the lifetime of the process.

Value: the same version string shown in the Moss boot report and
`GET /healthz` response. Example: `version=2.4.1`

### `health`

Emit the Stone's current health status. This is dynamic — it may change
during the process lifetime (e.g. after a failed dependency check).

Recommended values: `healthy`, `degraded`, `unhealthy`. These align
with the ASP.NET `HealthStatus` enum.

**Update mechanism:** When health status changes, Moss should
re-register (or update the TXT record) with the mDNS daemon. Koi and
mdns-sd handle re-registration as an update — no unregister/register
cycle needed. Consumers see a `resolved` event with updated TXT.

## Consequences

### Positive

- `DiscoveredStone.MossVersion` is populated — consumers can display
  version info in dashboards and detect version skew across Stones
  without health-checking each one individually.
- `DiscoveredStone.Health` is populated — the `KoiHandler` can filter
  unhealthy Stones from the failover candidate pool without making
  HTTP health probes. This is especially valuable in container
  environments where network round-trips to each Stone add latency.
- The discovery spec and live behavior converge — no more spec/reality
  gap.

### Neutral

- TXT record size increases by ~30-40 bytes. Well within the DNS TXT
  record limit (multiple 255-byte strings, up to ~64KB total). No
  practical impact on mDNS packet size.
- Health status changes trigger mDNS re-announcements. For typical
  Moss instances, health transitions are rare (startup, dependency
  failure, recovery). This is not a high-frequency update.

### Risks

- If `health` TXT updates are too frequent (e.g. tied to a
  high-frequency health check loop), mDNS traffic could spike.
  Mitigate by only re-registering on actual status transitions, not
  on every health check.

## Implementation notes

- The `version` field should be set once at Moss startup during
  `_moss._tcp` registration. No update needed.
- The `health` field should be set at startup (initial value:
  `healthy` after readiness checks pass) and updated on health status
  transitions via TXT record re-registration.
- Koi requires no changes — TXT records are passed through
  transparently in `ServiceRecord` responses and SSE events.

## References

- [DATA-0091 — Koi as authoritative topology handler](./DATA-0091-koi-authoritative-topology-handler.md)
- `zen-garden/docs/specs/discovery.md` — TXT schema definition
- `koi/TECHNICAL.md` — Koi wire protocol (TXT transparency)
