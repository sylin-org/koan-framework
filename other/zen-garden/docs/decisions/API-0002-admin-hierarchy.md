# API-0002: Admin API Hierarchy

**Status:** Accepted
**Date:** 2026-01-23
**Scope:** Administrative endpoints for moss daemon and stone machine control

---

## Context

The original API had administrative endpoints scattered across multiple locations:
- `/admin/take-root` - Windows service installation
- `/admin/shutdown` - Daemon shutdown (never registered in router)
- `/api/v1/stone/shutdown` - Also daemon shutdown (confusing name)

This created confusion between "stone" (the physical machine) and "moss" (the daemon process).

---

## Decision

Consolidate all administrative operations under a clear hierarchy:

```
/api/v1/admin/
├── moss/
│   ├── shutdown      # POST - Graceful daemon exit
│   └── take-root     # POST - Install as Windows service
└── stone/
    ├── shutdown      # POST - Machine power off
    ├── reboot        # POST - Machine restart
    └── :name/wake    # POST - Wake stone via WoL (rouse)
```

### Key Distinctions

| Target | Meaning | Example Operations |
|--------|---------|-------------------|
| `moss` | The daemon process | shutdown (exit), take-root (install service) |
| `stone` | The physical machine | shutdown (poweroff), reboot, wake (WoL) |

### CLI Mapping

| API Endpoint | Zen CLI | Normative CLI |
|--------------|---------|---------------|
| `/api/v1/admin/stone/shutdown` | `garden-rake slumber [stone]` | `garden-rake admin stone shutdown` |
| `/api/v1/admin/stone/reboot` | `garden-rake stir [stone]` | `garden-rake admin stone reboot` |
| `/api/v1/admin/stone/:name/wake` | `garden-rake rouse <stone>` | `garden-rake admin stone wake <stone>` |
| `/api/v1/admin/moss/shutdown` | - | `garden-rake admin moss shutdown` |
| `/api/v1/admin/moss/take-root` | - | `garden-rake admin moss take-root` |

---

## Removed Endpoints

The following endpoints are removed (no backwards compatibility):

| Removed | Replaced By |
|---------|-------------|
| `/admin/take-root` | `/api/v1/admin/moss/take-root` |
| `/api/v1/stone/shutdown` | `/api/v1/admin/moss/shutdown` |

Note: `/api/v1/stone/upgrade` and `/api/v1/stone/deploy` remain unchanged - they are software operations, not privileged admin operations.

---

## Implementation

### Platform-Specific Behavior

**Stone Shutdown:**
- Linux: `systemctl poweroff`
- Windows: `shutdown /s /t 0`

**Stone Reboot:**
- Linux: `systemctl reboot`
- Windows: `shutdown /r /t 0`

### Response Format

All admin endpoints return:
```json
{
  "success": true,
  "message": "Human-readable status message"
}
```

### Safety

- All admin endpoints require authentication (future: pond trust)
- Operations execute after 500ms delay to allow HTTP response
- Warn-level logging before destructive operations

---

## Wake-on-LAN (Rouse)

### Overview

The `stone/:name/wake` endpoint enables waking offline stones using Wake-on-LAN magic packets. This requires:

1. **MAC Address Discovery**: Stones announce their MAC address in UDP chirps and mDNS TXT records
2. **Offline Stone Tracking**: Topology cache marks stones as "offline" instead of evicting them, preserving MAC addresses

### Topology Cache Changes

Instead of TTL-based eviction, stones are now marked with a status:

| Status | Meaning | MAC Preserved |
|--------|---------|--------------|
| `Online` | Seen within 90 seconds | Yes |
| `Offline` | Not seen for 90+ seconds | Yes (for WoL) |

**Eviction Policy:**
- Max 64 offline stones tracked (LRU eviction when cap reached)
- Offline stones evicted after 24 hours
- No disk persistence (cache rebuilds on moss restart)

### WoL Magic Packet

Standard Wake-on-LAN format:
- 6 bytes of `0xFF` (synchronization stream)
- MAC address repeated 16 times
- Sent as UDP broadcast to ports 9 and 7

### Goodbye Announcement

Before shutting down or rebooting, a stone broadcasts a `STONE_GOODBYE` UDP announcement:
- Notifies other stones immediately (no need to wait for 90-second chirp timeout)
- Other stones mark the departing stone as "offline" instantly
- MAC address preserved in topology cache for future Wake-on-LAN

### Workflow

```
1. garden-rake observe         # Stone "oak" discovered with MAC
2. garden-rake slumber oak     # Stone sends goodbye, then shuts down
3. [other stones immediately mark "oak" as offline]
4. [hours later]
5. garden-rake rouse oak       # WoL packet sent using cached MAC
6. [stone boots, announces]
7. Stone "oak" status → Online
```

---

## Consequences

**Positive:**
- Clear separation of daemon vs machine operations
- Consistent `/api/v1/admin/` prefix for all privileged operations
- Extensible hierarchy for future admin operations
- Wake-on-LAN enables remote stone management without physical access
- Offline stone tracking preserves topology knowledge for recovery scenarios

**Negative:**
- Breaking change from original `/admin/` routes
- Requires client updates
- WoL requires hardware support (BIOS/UEFI and NIC configuration)
- MAC address not available on Windows stones (Linux-only for now)

**Mitigated by:** Greenfield status - no deployed clients depend on old routes.
