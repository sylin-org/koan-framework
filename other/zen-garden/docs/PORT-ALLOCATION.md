# Zen Garden Port Allocation

**Baseline Port:** 7184 (GRDN - phone keypad mapping G=7, R=18, D=4)

**Date:** January 16, 2026  
**Status:** Active  
**Purpose:** Centralized port registry for all Zen Garden network services

---

## Port Allocation Table

| Port | Service | Protocol | Purpose | Status |
|------|---------|----------|---------|--------|
| **7184** | P2P Discovery | UDP | Stone-to-Stone peer discovery broadcasts | ✅ Active |
| **7185** | Garden-Moss HTTP API | HTTP/TCP | Stone management API endpoint | ✅ Active |
| **7186** | Garden-Lantern Registry | HTTP/TCP | Centralized service registry and topology API | 🔜 Planned |
| **7187** | Garden-Lantern Election | UDP | Multi-active Garden-Lantern Election and health announcements | 🔜 Planned |
| **7188-7199** | Reserved | - | Future Zen Garden infrastructure services | 📦 Reserved |

---

## Port Details

### 7184 - P2P Discovery (UDP)

**Current Name:** "UDP port 3004"  
**Function:** Peer-to-peer stone discovery via broadcast messages  
**Listeners:** All moss instances  
**Message Types:**
- `DiscoveryRequest` - Broadcast from rake to find available stones
- `DiscoveryResponse` - Unicast response from moss with stone capabilities

**Implementation Files:**
- Listener: `src/linux/moss/src/discovery.rs` - `udp_listener()` function
- Sender: `src/windows/garden-rake/src/discovery.rs` - `discover_moss()` function

**Example Traffic:**
```
Rake → 255.255.255.255:7184 (broadcast)
  {"request_id": "uuid", "requester": "rake-client"}

Moss → Rake IP:ephemeral (unicast)
  {"stone_name": "stone-01", "stone_endpoint": "http://192.168.1.100:7185"}
```

---

### 7185 - Garden-Moss HTTP API (TCP)

**Current Name:** "Port 3001"  
**Function:** Primary stone management HTTP API  
**Protocol:** HTTP/1.1  
**Endpoints:**
- `/health` - Liveness probe
- `/capabilities` - Hardware capabilities query
- `/metrics` - Prometheus metrics
- `/api/services` - List running offerings
- `/api/operations/offer/:offering` - Start new service
- `/api/operations/remove/:target` - Stop service
- `/admin/shutdown` - Graceful daemon shutdown

**Configuration Priority:**
1. CLI argument: `garden-moss --port 7185`
2. Environment variable: `PORT=7185`
3. Config file: `/etc/zen-garden/garden-moss.toml` → `port = 7185`
4. Default: `7185`

**Implementation Files:**
- Server: `src/linux/moss/src/main.rs` - Axum HTTP server
- Default: Line ~2036 → `.unwrap_or(7185)`
- Config: Line ~81-100 → `MossConfig` struct

**Security:**
- Bind: `0.0.0.0:7185` (all interfaces)
- Authentication: Bearer tokens (HMAC-SHA256 JWT) when pond mode active
- CORS: Disabled (internal network only)

---

### 7186 - Garden-Lantern Registry (TCP)

**Status:** Planned (Phase 1 implementation)  
**Function:** Centralized service registry and topology management  
**Protocol:** HTTP/1.1  
**Endpoints:**
- `POST /api/register` - Stone heartbeat registration
- `GET /api/resolve?service={type}` - Service discovery
- `GET /api/stones` - Full topology query
- `GET /api/topology` - Topology sync (active Lantern only)
- `GET /api/health` - Health check
- `GET /api/events/stream` - SSE event stream

**Configuration:**
- Default port: `7186`
- Environment variable: `LANTERN_PORT=7186`
- Config file: `/etc/zen-garden/lantern.toml` → `port = 7186`

**Implementation Files:**
- Planned: `src/lantern/src/main.rs`
- Proposal: `docs/proposals/LANTERN-SERVICE-PROPOSAL.md`

---

### 7187 - Garden-Lantern Election (UDP)

**Status:** Planned (Phase 1 implementation)  
**Function:** Multi-active Garden-Lantern Election and health announcements  
**Protocol:** UDP broadcast  
**Message Types:**
- `LANTERN_ANNOUNCEMENT` - Active Lantern health signal (every 10s)
- `LANTERN_DISCOVERY` - New primary requests stone re-registration

**Election Protocol:**
- Dormant Lanterns listen on 7187
- Active Lanterns broadcast on 7187 every 10s
- Election delay: `blake3::hash(lantern_name + lan_ip + announcement_id)[0] * 10ms`
- Suppression: Candidates hearing announcement suppress their own

**Implementation Files:**
- Planned: `src/lantern/src/election.rs`

---

## Migration Notes

### Current Ports → New Ports

| Old Port | New Port | Service | Migration Action |
|----------|----------|---------|------------------|
| 3004 | 7184 | UDP Discovery | Update bind and broadcast addresses |
| 3001 | 7185 | Moss HTTP | Update default, docs, config examples |
| 3000 | 7186 | Lantern HTTP | Use in implementation (not yet deployed) |
| N/A | 7187 | Garden-Lantern Election | New allocation for Phase 1 |

### Files Requiring Updates

**Code:**
- `src/linux/moss/src/discovery.rs` - Line 7: `bind("0.0.0.0:7184")`
- `src/windows/garden-rake/src/discovery.rs` - Line 20: `send_to("255.255.255.255:7184")`
- `src/linux/moss/src/main.rs` - Line ~2036: `.unwrap_or(7185)`
- `src/linux/moss/src/main.rs` - Line ~1931: Graceful shutdown HTTP request to `7185`

**Documentation:**
- `docs/TECHNICAL-SPEC.md` - All references to ports 3001, 3002, 3003, 3004, 3000
- `docs/proposals/LANTERN-SERVICE-PROPOSAL.md` - All references to port 3000 → 7186
- `DEVELOPMENT-PLAN.md` - Docker Compose examples with old ports

**Configuration Examples:**
- `docs/TECHNICAL-SPEC.md` - Line 297: `port = 7185` in TOML example
- Installer scripts referencing default ports
- Docker Compose files in test directories

---

## Port Range Rationale

**7184-7199 (16 ports)** reserved for Zen Garden infrastructure:
- **7184**: GRDN baseline (phone keypad)
- **7185-7187**: Core services (Moss, Lantern HTTP, Lantern UDP)
- **7188-7199**: Future expansion (e.g., metrics aggregator, distributed logs, federation)

**Semantic Meaning:**
- 7184 = GRDN (Garden)
- Memorable, easy to communicate
- Avoids common conflicts (PostgreSQL 5432, Redis 6379, MongoDB 27017, Grafana 3000)

---

## Firewall Rules

**For stone-to-stone communication:**
```bash
# UDP P2P Discovery (broadcast)
sudo ufw allow 7184/udp comment "Zen Garden P2P discovery"

# Garden-Moss HTTP API (TCP)
sudo ufw allow 7185/tcp comment "Zen Garden Moss API"

# Garden-Lantern Registry (TCP)
sudo ufw allow 7186/tcp comment "Zen Garden Garden-Lantern Registry"

# Garden-Lantern Election (UDP)
sudo ufw allow 7187/udp comment "Zen Garden Garden-Lantern Election"
```

**Windows Firewall (PowerShell):**
```powershell
# UDP P2P Discovery
New-NetFirewallRule -DisplayName "Zen Garden P2P Discovery" -Direction Inbound -Protocol UDP -LocalPort 7184 -Action Allow

# Garden-Moss HTTP API
New-NetFirewallRule -DisplayName "Zen Garden Moss API" -Direction Inbound -Protocol TCP -LocalPort 7185 -Action Allow

# Garden-Lantern Registry
New-NetFirewallRule -DisplayName "Zen Garden Garden-Lantern Registry" -Direction Inbound -Protocol TCP -LocalPort 7186 -Action Allow

# Garden-Lantern Election
New-NetFirewallRule -DisplayName "Zen Garden Garden-Lantern Election" -Direction Inbound -Protocol UDP -LocalPort 7187 -Action Allow
```

---

## Testing Port Availability

**Check if port is in use:**
```bash
# Linux
sudo ss -tulpn | grep 7185
sudo lsof -i :7185

# Windows
netstat -ano | findstr :7185
```

**Test UDP broadcast:**
```bash
# Sender (rake simulation)
echo '{"request_id":"test"}' | nc -u -b 255.255.255.255 7184

# Receiver (moss simulation)
nc -ul 7184
```

**Test HTTP endpoint:**
```bash
curl http://localhost:7185/health
```

---

## Reserved Port Expansion Ideas

**Future Services (7188-7199):**
- **7188**: Metrics aggregator (Prometheus exporter)
- **7189**: Distributed logging (centralized logs collector)
- **7190**: Federation gateway (multi-garden coordination)
- **7191**: Backup coordinator (snapshot orchestration)
- **7192**: MCP gateway (Model Context Protocol proxy)
- **7193-7199**: Reserved for future infrastructure

---

## Version History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2026-01-16 | 1.0 | Initial port allocation (7184-7187) | Architecture Team |

---

## See Also

- [LANTERN-SERVICE-PROPOSAL.md](proposals/LANTERN-SERVICE-PROPOSAL.md) - Lantern architecture
- [SECURITY-SPEC.md](SECURITY-SPEC.md) - Bearer token authentication
- [TECHNICAL-SPEC.md](TECHNICAL-SPEC.md) - Moss API reference
