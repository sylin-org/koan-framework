# Zen Garden Bridge Specification

**Garden-to-garden federation for distributed capability sharing**

**Status:** Proposal  
**Date:** January 2026  
**Authors:** Collaborative design session

---

## Table of Contents

1. [Overview](#overview)
2. [The Garden Principle](#the-garden-principle)
3. [Why Bridges Exist](#why-bridges-exist)
4. [Bridges vs Ponds](#bridges-vs-ponds)
5. [The Desire Ledger](#the-desire-ledger)
6. [Bridge Lifecycle](#bridge-lifecycle)
7. [Policies](#policies)
8. [Resolution Across Bridges](#resolution-across-bridges)
9. [Bridge Health](#bridge-health)
10. [Security Model](#security-model)
11. [CLI Reference](#cli-reference)
12. [API Specification](#api-specification)
13. [Configuration](#configuration)

---

## Overview

### What is a Bridge?

A **Bridge** is a secure, policy-controlled connection between two Zen Gardens that allows them to share capabilities. Apps in one garden can discover and use offerings from a bridged garden as if they were local.

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   home-lab (your garden)              gpu-farm (friend's)       │
│   ┌─────────────────┐                 ┌─────────────────┐       │
│   │  stone-01       │                 │  stone-gpu-01   │       │
│   │  stone-02       │     BRIDGE      │  stone-gpu-02   │       │
│   │  stone-03       │ ◄═════════════► │  stone-gpu-03   │       │
│   │                 │   ai:*, storage │                 │       │
│   └─────────────────┘                 └─────────────────┘       │
│                                                                 │
│   Your app asks for             gpu-farm serves it              │
│   "zen-garden:nomic-embed-text" transparently                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Core Characteristics

1. **Garden-to-Garden** — Bridges connect gardens, not individual stones
2. **Capability-Based** — Policies filter by capability, not topology
3. **Bidirectional Potential** — Each side controls what it shares and accepts
4. **mTLS Secured** — Keystone-to-Keystone trust, encrypted tunnel
5. **Transparent to Apps** — Resolution works the same, just spans further

### What Bridges Enable

| Scenario | Without Bridge | With Bridge |
|----------|----------------|-------------|
| Friend has GPU, you don't | Can't use AI features | Your apps gain AI seamlessly |
| Office needs home storage | Manual file transfer | Direct access via policy |
| Classroom collaboration | Isolated islands | Shared capability pool |
| Disaster recovery | Manual failover | Automatic federated resolution |

---

## The Garden Principle

### Gardens Are the Unit of Everything

| Concept | Scope | Meaning |
|---------|-------|---------|
| **Trust** | Garden (Pond) | Stones in a garden trust each other |
| **Operations** | Garden | Ceremonies coordinate within a garden |
| **Identity** | Garden | Names, Keystones, topology |
| **Administration** | Garden | One admin team, one policy set |

**Stones are internal implementation details.** Apps ask for capabilities ("give me mongodb"), not infrastructure ("give me stone-02's mongodb"). The garden decides which stone serves the request.

### Why Not Bridge to Individual Stones?

If we allowed stone-level bridging:

```
❌ BAD: "Bridge to gpu-farm's stone-gpu-02 specifically"
```

Problems:
1. **Leaky abstraction** — Remote topology becomes your concern
2. **Brittle** — What if stone-gpu-02 dies? Your bridge breaks.
3. **Trust confusion** — Do you trust the stone or the garden?
4. **Operational coupling** — Their internal migrations break your setup

Instead:

```
✓ GOOD: "Bridge to gpu-farm, accept ai:* capabilities"
```

Benefits:
1. **Clean abstraction** — You see capabilities, not infrastructure
2. **Resilient** — gpu-farm handles its own stones
3. **Clear trust** — You trust gpu-farm's Keystone
4. **Decoupled** — Their internal changes don't affect you

### The Rule

**You never bridge to a stone. You bridge to a garden and filter by capability.**

If you need different trust boundaries within your infrastructure, you need different gardens. A stone belongs to exactly one garden.

---

## Why Bridges Exist

### The Distributed Capability Problem

Your home lab has:
- mongodb (database)
- redis (cache)
- minio (storage)

Your friend's setup has:
- ollama with 70B model (AI inference)
- 48GB VRAM GPU

Your app wants semantic search. It *craves* AI embeddings. Your garden can't provide it. Your friend's garden can.

**Without bridges:** You can't use your friend's GPU. You'd have to:
- Expose their services to the internet (security nightmare)
- Set up VPNs and manual configuration (complexity nightmare)
- Give up on the feature (sad)

**With bridges:** 
```bash
garden-rake bridge with gpu-farm
# TOTP authentication
# Policy negotiation
# Done — your apps can now use AI
```

### Federation, Not Centralization

Bridges are peer-to-peer agreements between gardens. There's no central registry of gardens. No authority deciding who can connect to whom.

```
      garden-A ◄────► garden-B
          ▲               ▲
          │               │
          ▼               ▼
      garden-C ◄────► garden-D
```

Each bridge is independent. If garden-A bridges to garden-B and garden-B bridges to garden-C, garden-A does NOT automatically see garden-C. Transitive trust is explicit, not implicit.

---

## Bridges vs Ponds

Both provide secure connections. Different scope.

| Aspect | Pond | Bridge |
|--------|------|--------|
| **Scope** | Within a garden | Between gardens |
| **Participants** | Stones | Gardens (via Keystones) |
| **Trust model** | Full trust (same admin) | Partial trust (policy-filtered) |
| **Setup** | `garden-rake place keystone` | `garden-rake bridge with` |
| **Authentication** | TOTP (stone admission) | TOTP (garden pairing) |
| **Data flow** | Unrestricted within garden | Policy-controlled |
| **Topology** | Visible to all stones | Hidden across bridge |

**Pond = internal security. Bridge = external federation.**

### Relationship

```
┌──────────────────────────────────────┐
│            GARDEN A                  │
│  ┌──────────────────────────────┐    │
│  │           POND A             │    │
│  │   stone-1 ◄──► stone-2      │    │
│  │       ▲           ▲         │    │
│  │       └─────┬─────┘         │    │
│  │         (mTLS)              │    │
│  └──────────────────────────────┘    │
│              ▲                       │
│              │ Keystone A            │
└──────────────┼───────────────────────┘
               │
         B R I D G E  (mTLS between Keystones)
               │
┌──────────────┼───────────────────────┐
│              │ Keystone B            │
│              ▼                       │
│  ┌──────────────────────────────┐    │
│  │           POND B             │    │
│  │   stone-3 ◄──► stone-4      │    │
│  └──────────────────────────────┘    │
│            GARDEN B                  │
└──────────────────────────────────────┘
```

A garden without a Pond can still bridge (Keystones are generated for the bridge). But a Pond provides the internal security foundation.

---

## The Desire Ledger

### Unified Inbox for Garden Wants

Gardens track **desires** — things that have been requested but not yet fulfilled. The desire ledger is a unified view of:

1. **Offering Desires** — Capabilities apps are craving
2. **Bridge Desires** — Other gardens requesting federation
3. **Stone Desires** — Devices requesting Pond admission

```bash
$ garden-rake desires

DESIRES                                    
───────────────────────────────────────────────
OFFERINGS (apps crave capabilities)
    ai:embeddings        2 apps         → semantic-search, smart-tags
    ai:vision            1 app          → image-search
    storage:s3           1 app          → cloud-backup

BRIDGES (gardens want to connect)
    gpu-farm             pending        received 3m ago
                         wants from us: (nothing)
                         offers to us:  ai:*
                         
    backup-site          pending        received 1h ago
                         wants from us: storage:*
                         offers to us:  storage:* (offsite)

STONES (devices want to join pond)
    unknown-device       pending        TOTP: 7X9K2M
                         192.168.1.47
                         4 cores, 8GB RAM
```

### Desire Sources

**Offering desires** come from apps:
```python
# App registers what it wants
garden.crave("ai:embeddings", unlocks="semantic-search")
garden.crave("ai:vision", unlocks="image-search")

# App registers what it requires (hard dependency)
garden.require("mongodb")
```

**Bridge desires** come from remote gardens:
```bash
# Remote garden initiates
$ garden-rake bridge with home-lab
# This creates a bridge desire in home-lab's ledger
```

**Stone desires** come from devices:
```bash
# New stone requests admission
$ garden-rake place stone
# Creates stone desire in garden's ledger, shows TOTP
```

### Fulfilling Desires

```bash
# Fulfill offering desire
$ garden-rake offer ollama
✓ ollama planted on stone-02
✓ Fulfilled desires: ai:embeddings (2 apps), ai:vision (1 app)
  Apps notified of new capabilities

# Fulfill bridge desire  
$ garden-rake accept bridge from gpu-farm
✓ Bridge ceremony initiated
✓ Bridge active — ai:* now available via gpu-farm
✓ Fulfilled desires: ai:embeddings (2 apps), ai:vision (1 app)

# Fulfill stone desire
$ garden-rake accept stone 7X9K2M
Enter your TOTP code: ______
✓ unknown-device admitted to pond
✓ Renamed to stone-04
```

### Why Track Desires?

1. **Visibility** — Know what apps are hungry for
2. **Planning** — Prioritize hardware purchases based on demand
3. **Automation** — Future: auto-accept bridges from trusted sources
4. **Metrics** — Understand garden utilization and gaps

---

## Bridge Lifecycle

### States

```
┌─────────────┐
│  INITIATING │ ← Requester sends bridge request
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   PENDING   │ ← Appears in acceptor's desires
└──────┬──────┘
       │
       ├────────────────┐
       ▼                ▼
┌─────────────┐  ┌─────────────┐
│  ACCEPTED   │  │  REJECTED   │
└──────┬──────┘  └─────────────┘
       │
       ▼
┌─────────────┐
│  CEREMONY   │ ← Keystone exchange, policy negotiation
└──────┬──────┘
       │
       ├────────────────┐
       ▼                ▼
┌─────────────┐  ┌─────────────┐
│   ACTIVE    │  │   FAILED    │
└──────┬──────┘  └─────────────┘
       │
       ▼
┌─────────────┐
│   BURNED    │ ← Bridge deliberately severed
└─────────────┘
```

### Phase 1: Initiation

**Initiator requests bridge:**

```bash
$ garden-rake bridge with gpu-farm

BRIDGE REQUEST
───────────────────────────────────────────────
  Target garden:     gpu-farm
  Target address:    gpu-farm.example.com:7190
  
  Enter your TOTP code: 847291
  
Sending bridge request...
✓ Request sent

Your request is now pending in gpu-farm's desires.
An operator there must accept it.

Check status: garden-rake bridges
```

**What's sent:**
```json
{
  "type": "BRIDGE_REQUEST",
  "from_garden": "home-lab",
  "from_keystone_fingerprint": "sha256:abc123...",
  "proposed_policy": {
    "we_want": ["ai:*"],
    "we_offer": ["database:*", "storage:*"]
  },
  "totp_proof": "HMAC(shared_secret, timestamp, 'bridge-request')",
  "timestamp": "2026-01-20T15:30:00Z",
  "expires": "2026-01-21T15:30:00Z"
}
```

### Phase 2: Pending

**Request appears in acceptor's desires:**

```bash
$ garden-rake desires

BRIDGES (gardens want to connect)
    home-lab             pending        received 3m ago
                         wants from us: ai:*
                         offers to us:  database:*, storage:*
                         keystone:      sha256:abc123...
                         expires:       23h 57m
```

**Acceptor can inspect before deciding:**

```bash
$ garden-rake inspect bridge home-lab

BRIDGE REQUEST: home-lab
───────────────────────────────────────────────
  Received:        3 minutes ago
  Expires:         23h 57m
  
  Their keystone:  sha256:abc123def456...
  Their address:   home-lab.local:7190
  
  PROPOSED EXCHANGE
  ─────────────────
  They want from us:
    ai:*                    (we have: ollama, localai)
    
  They offer to us:
    database:*              (they have: mongodb, postgresql)
    storage:*               (they have: minio)
    
  ESTIMATED IMPACT
  ─────────────────
  If accepted:
    • Our ollama/localai available to their apps
    • Their mongodb/postgresql/minio available to our apps
    • Traffic: ~50ms added latency (same city estimate)
    
Accept? garden-rake accept bridge from home-lab
Reject? garden-rake reject bridge from home-lab
```

### Phase 3: Acceptance

**Acceptor approves:**

```bash
$ garden-rake accept bridge from home-lab

ACCEPT BRIDGE
───────────────────────────────────────────────
  From:            home-lab
  
  Enter your TOTP code: 293847
  
Verifying...
✓ TOTP verified
✓ Bridge ceremony starting
```

### Phase 4: Ceremony

The bridge ceremony establishes trust between Keystones.

```
┌─────────────────────────────────────────────────────────────────┐
│                      BRIDGE CEREMONY                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  home-lab (initiator)               gpu-farm (acceptor)         │
│                                                                 │
│  Phase 1: TOTP Verification (already done on both sides)        │
│                                                                 │
│  Phase 2: Keystone Exchange                                     │
│  ─────────────────────────────────────────────────────────      │
│     "Here's my Keystone public cert"  ───────────────────►      │
│     ◄───────────────────  "Here's my Keystone public cert"      │
│     Both sides verify fingerprints match expected               │
│                                                                 │
│  Phase 3: Policy Negotiation                                    │
│  ─────────────────────────────────────────────────────────      │
│     "I want ai:*, I offer database:*" ───────────────────►      │
│     ◄───────────────────  "I accept, I want nothing, I offer    │
│                            ai:*"                                │
│     Policies recorded on both sides                             │
│                                                                 │
│  Phase 4: Tunnel Establishment                                  │
│  ─────────────────────────────────────────────────────────      │
│     mTLS connection established between Lanterns                │
│     Heartbeat initiated (every 30s)                             │
│                                                                 │
│  Phase 5: Offering Announcement                                 │
│  ─────────────────────────────────────────────────────────      │
│     "I have: mongodb, postgresql, minio" ────────────────►      │
│     ◄────────────────  "I have: ollama (ai:embeddings,          │
│                         ai:vision, ai:chat)"                    │
│     Both sides update federated offering cache                  │
│                                                                 │
│  Phase 6: Completion                                            │
│  ─────────────────────────────────────────────────────────      │
│     ✓ Bridge active                    ✓ Bridge active          │
│     Notify local apps of new capabilities                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Ceremony record:**

```yaml
ceremony:
  id: bridge-homelab-gpufarm-20260120-1535
  type: bridge
  
  participants:
    initiator:
      garden: home-lab
      keystone: sha256:abc123...
      address: home-lab.local:7190
    acceptor:
      garden: gpu-farm
      keystone: sha256:def456...
      address: gpu-farm.example.com:7190
      
  phases:
    - name: totp_verification
      status: completed
      duration_ms: 1200
      
    - name: keystone_exchange
      status: completed
      duration_ms: 340
      
    - name: policy_negotiation
      status: completed
      duration_ms: 120
      agreed_policy:
        home-lab:
          receives: [ai:*]
          shares: [database:*, storage:*]
        gpu-farm:
          receives: []
          shares: [ai:*]
          
    - name: tunnel_establishment
      status: completed
      duration_ms: 890
      protocol: mTLS-1.3
      
    - name: offering_announcement
      status: completed
      duration_ms: 230
      
  completed_at: 2026-01-20T15:35:45Z
  result: active
```

### Phase 5: Active

Bridge is operational. Apps can resolve federated offerings.

```bash
$ garden-rake bridges

BRIDGES
───────────────────────────────────────────────
  gpu-farm         active ↔        latency: 48ms
                   since: 2h ago
                   receiving: ai:embeddings, ai:vision, ai:chat
                   sharing:   (nothing)
                   
  backup-site      active ↔        latency: 12ms  
                   since: 14d ago
                   receiving: storage:backup
                   sharing:   storage:*
```

### Phase 6: Burning

Deliberately severing a bridge.

```bash
$ garden-rake burn bridge to gpu-farm

BURN BRIDGE
───────────────────────────────────────────────
  Target: gpu-farm
  
  ⚠ WARNING
  This will:
    • Disconnect from gpu-farm immediately
    • Revoke their trust in your Keystone
    • Remove federated offerings from your apps
    • Require full ceremony to reconnect
    
  Apps currently using federated offerings:
    • semantic-search-app (ai:embeddings)
    • image-tagger (ai:vision)
    
  These apps will lose capabilities.
  
Burn bridge? [y/N]: y

Enter your TOTP code: 192837

Burning...
✓ Bridge to gpu-farm burned
✓ Apps notified of capability loss
```

---

## Policies

### Policy Structure

```yaml
bridge:
  garden: gpu-farm
  
  inbound:
    # What we accept from them
    allow:
      - category: ai           # Accept all AI offerings
        
    deny:
      - offering: sketchy-ai   # Except this specific one
      
    requirements:
      # Quality gates for accepted offerings
      - capability: ai.vision
        min_vram_gb: 16        # Only accept high-end models
        
  outbound:
    # What we share with them
    allow:
      - category: database
      - offering: minio        # Specific offering
        
    deny:
      - offering: admin-db     # Don't share internal databases
```

### Policy Types

**Category-based:**
```yaml
allow:
  - category: ai       # Accept all ai:* capabilities
  - category: storage  # Accept all storage:* capabilities
```

**Offering-specific:**
```yaml
allow:
  - offering: mongodb      # Only this specific offering
  - offering: postgresql
```

**Wildcard:**
```yaml
allow:
  - category: "*"    # Accept everything (use with caution)

deny:
  - category: "*"    # Share nothing (secure default)
```

### Default Policies

**Secure defaults:**
```toml
[federation.default_policy]
inbound_allow = []         # Accept nothing by default
inbound_deny = ["*"]       # Explicit deny-all

outbound_allow = []        # Share nothing by default
outbound_deny = ["*"]      # Explicit deny-all
```

You must explicitly allow capabilities in both directions.

### Policy Negotiation

During bridge ceremony, both sides propose policies:

```
home-lab proposes:
  inbound: [ai:*]
  outbound: [database:*, storage:*]

gpu-farm proposes:
  inbound: []
  outbound: [ai:*]

Result (intersection):
  home-lab receives: [ai:*] from gpu-farm
  home-lab shares:   [database:*, storage:*] with gpu-farm
  gpu-farm receives: [database:*, storage:*] from home-lab
  gpu-farm shares:   [ai:*] with home-lab
```

If there's no intersection (neither side wants what the other offers), the bridge still succeeds but does nothing useful. Future policy updates can activate it.

### Policy Updates

```bash
# Add capability to outbound policy
$ garden-rake bridge to gpu-farm share cache:*

BRIDGE POLICY UPDATE
───────────────────────────────────────────────
  Bridge:     gpu-farm
  Change:     Add to outbound allow: cache:*
  
  Current outbound: [database:*, storage:*]
  New outbound:     [database:*, storage:*, cache:*]
  
Apply? [y/N]: y

Enter your TOTP code: 482910

✓ Policy updated
✓ gpu-farm notified of new capabilities
✓ redis now available to gpu-farm apps
```

Policy updates require TOTP (security-sensitive operation).

---

## Resolution Across Bridges

### How Apps See Federated Offerings

Apps use the same resolution API regardless of whether offerings are local or federated:

```python
# App code (unchanged whether local or federated)
connection_string = garden.resolve("mongodb")
# Returns: mongodb://stone-02.local:27017

embedding_service = garden.resolve("nomic-embed-text")
# Could return:
#   - Local: http://stone-03.local:11435 (if we have it)
#   - Federated: federated://gpu-farm/ollama/nomic-embed-text (if via bridge)
```

### Resolution Priority

When multiple sources provide the same capability:

```
1. Local offerings (lowest latency)
2. Bridged offerings (sorted by health and latency)
3. Fallback: return unavailable error
```

**Example:**

```
App requests: ai:embeddings

Available sources:
  - Local: none
  - Bridge: gpu-farm (latency 48ms, healthy)
  - Bridge: ai-cluster (latency 120ms, degraded)
  
Resolution: gpu-farm (healthiest bridge)
```

### Federated Connection Strings

Connection strings for federated offerings use a special protocol:

```
federated://gpu-farm/ollama/nomic-embed-text?token=eyJ...
    │         │       │          │              │
    │         │       │          │              └─ Short-lived auth token
    │         │       │          └─ Specific model/endpoint
    │         │       └─ Offering name on remote garden
    │         └─ Garden name (for routing)
    └─ Protocol indicator (handled by Lantern)
```

**The app doesn't parse this.** It passes the whole string to the Zen Garden client library, which handles routing through the bridge.

---

## Bridge Health

### Monitoring

```bash
$ garden-rake bridges --health

BRIDGE HEALTH
───────────────────────────────────────────────
  gpu-farm         healthy ✓       latency: 48ms (p50), 62ms (p99)
                   uptime: 14d 3h
                   last heartbeat: 2s ago
                   requests today: 1,247
                   
  backup-site      degraded ⚠      latency: 340ms (p50), 2100ms (p99)
                   uptime: 6h (reconnected after outage)
                   last heartbeat: 28s ago
                   requests today: 89
                   note: High latency, possible network issue
```

### Heartbeat Protocol

Bridges maintain liveness through heartbeats:

```json
{
  "type": "BRIDGE_HEARTBEAT",
  "from": "home-lab",
  "timestamp": "2026-01-20T16:00:00Z",
  "stats": {
    "requests_since_last": 42,
    "avg_latency_ms": 51,
    "errors_since_last": 0
  }
}
```

**Heartbeat interval:** 30 seconds  
**Timeout threshold:** 90 seconds (3 missed heartbeats)

### Degradation Handling

When a bridge becomes unhealthy:

```
STATE: HEALTHY
  └─ Latency normal, heartbeats received
  
STATE: DEGRADED
  └─ High latency (>500ms p50) OR
  └─ Missed 1 heartbeat OR  
  └─ Error rate >5%
  └─ Apps warned, still functional
  
STATE: UNHEALTHY
  └─ Missed 2 heartbeats OR
  └─ Error rate >25%
  └─ Apps notified, resolution falls back
  
STATE: DISCONNECTED
  └─ Missed 3+ heartbeats OR
  └─ Connection refused
  └─ Federated offerings unavailable
  └─ Auto-reconnect attempts begin
```

### App Notification

Apps subscribed to capability changes receive bridge health events:

```python
garden.subscribe("capabilities")

# Events received:
{"event": "bridge_degraded", "garden": "gpu-farm", "latency_ms": 520}
{"event": "bridge_disconnected", "garden": "gpu-farm"}
{"event": "capability_unavailable", "capability": "ai:embeddings", "reason": "bridge_disconnected"}
{"event": "bridge_reconnected", "garden": "gpu-farm"}
{"event": "capability_available", "capability": "ai:embeddings", "via": "gpu-farm"}
```

### Auto-Reconnect

When a bridge disconnects, the system attempts reconnection:

```
Disconnect detected
  └─ Wait 5s, attempt 1
      └─ Failed? Wait 15s, attempt 2
          └─ Failed? Wait 45s, attempt 3
              └─ Failed? Wait 2m, attempt 4
                  └─ Failed? Mark as DORMANT
                      └─ Retry every 10m
                      └─ Notify operator
```

No TOTP required for reconnection (Keystones already trust each other). Full re-ceremony only required if Keystone changes.

---

## Security Model

### Trust Establishment

**TOTP on both sides:**
- Initiator enters TOTP when sending request
- Acceptor enters TOTP when accepting request
- Proves human operator involvement on both ends
- Uses same Google Authenticator flow as Pond admission

**Keystone exchange:**
- Public certificates exchanged during ceremony
- Fingerprints verified against expected values
- Future communication authenticated via mTLS

### Encryption

**In transit:**
- All bridge traffic over mTLS 1.3
- Certificate pinning (only accept known Keystone)
- Perfect forward secrecy

**Request tokens:**
- Short-lived JWT (5 minute expiry)
- Signed by requesting garden's Keystone
- Validated by receiving garden's Lantern
- Single-use nonce prevents replay

### Policy Enforcement

**Outbound (what we share):**
- Our Lantern checks outbound policy before revealing offerings
- Remote garden only sees what we allow
- Can't enumerate our full offering list

**Inbound (what we accept):**
- Our Lantern checks inbound policy before returning federated results
- Apps only see capabilities we explicitly allow
- Can't discover other garden's internal topology

### Threat Model

**Threats bridges defend against:**

| Threat | Mitigation |
|--------|-----------|
| Unauthorized garden connection | TOTP required on both sides |
| Man-in-the-middle | mTLS with certificate pinning |
| Replay attacks | Single-use nonces, short-lived tokens |
| Capability enumeration | Policy enforcement, no free discovery |
| Topology exposure | Gardens abstract stones, reveal only capabilities |

**Threats bridges DON'T defend against:**

| Threat | Why Not Defended | Mitigation |
|--------|------------------|------------|
| Malicious offering behavior | Garden trusts what it accepts | Vet bridges before connecting |
| Resource exhaustion | No rate limiting (yet) | Monitor bridge health |
| Data exfiltration | Apps can send data anywhere | Trust the garden, audit policies |

### Trust Model

**Three levels:**

1. **Full trust (Pond)** — Stones in same garden trust each other completely
2. **Partial trust (Bridge)** — Gardens trust each other for specific capabilities
3. **Zero trust (Internet)** — Everything else

Bridges are **not VPNs**. They don't extend your network. They extend your capability space, with explicit policy control.

---

## CLI Reference

### Bridge Management

```bash
# Initiate bridge request
garden-rake bridge with <garden-name>
garden-rake bridge with <garden-name> at <address:port>

# List desires (including pending bridge requests)
garden-rake desires

# Inspect bridge request before accepting
garden-rake inspect bridge <garden-name>

# Accept bridge request
garden-rake accept bridge from <garden-name>

# Reject bridge request
garden-rake reject bridge from <garden-name>

# List active bridges
garden-rake bridges
garden-rake bridges --health

# View specific bridge details
garden-rake bridge to <garden-name>

# Update bridge policy
garden-rake bridge to <garden-name> share <capability>
garden-rake bridge to <garden-name> accept <capability>
garden-rake bridge to <garden-name> stop sharing <capability>
garden-rake bridge to <garden-name> stop accepting <capability>

# Burn (sever) bridge
garden-rake burn bridge to <garden-name>

# Normative aliases
garden-rake bridges create <garden-name>
garden-rake bridges list
garden-rake bridges show <garden-name>
garden-rake bridges delete <garden-name>
```

### Examples

```bash
# Simple bridge request
$ garden-rake bridge with gpu-farm
Enter your TOTP code: ______
✓ Request sent to gpu-farm

# Accept incoming request
$ garden-rake desires
# See pending request from home-lab
$ garden-rake inspect bridge home-lab
# Review details
$ garden-rake accept bridge from home-lab
Enter your TOTP code: ______
✓ Bridge ceremony started

# Monitor bridges
$ garden-rake bridges --health
gpu-farm    healthy ✓    48ms (p50)
backup-site degraded ⚠   340ms (p50)

# Update policy to share cache
$ garden-rake bridge to gpu-farm share cache:*
Enter your TOTP code: ______
✓ Policy updated

# Sever bridge
$ garden-rake burn bridge to gpu-farm
Burn bridge? [y/N]: y
Enter your TOTP code: ______
✓ Bridge burned
```

---

## API Specification

### Bridge Request Endpoints

**Initiate bridge request:**
```http
POST /api/v1/bridges/request
Content-Type: application/json

{
  "garden": "gpu-farm",
  "address": "gpu-farm.example.com:7190",
  "proposed_policy": {
    "inbound_allow": ["ai:*"],
    "outbound_allow": ["database:*", "storage:*"]
  },
  "totp": "847291"
}

Response 202 Accepted:
{
  "request_id": "bridge-req-home-gpu-20260120",
  "status": "pending",
  "expires_at": "2026-01-21T15:30:00Z"
}
```

**Accept bridge request:**
```http
POST /api/v1/bridges/accept
Content-Type: application/json

{
  "garden": "home-lab",
  "totp": "293847"
}

Response 200:
{
  "ceremony_id": "bridge-homelab-gpufarm-20260120",
  "status": "ceremony_started"
}
```

**List bridges:**
```http
GET /api/v1/bridges

Response 200:
{
  "bridges": [
    {
      "garden": "gpu-farm",
      "status": "active",
      "established_at": "2026-01-20T15:35:45Z",
      "health": "healthy",
      "latency_p50_ms": 48,
      "inbound_capabilities": ["ai:embeddings", "ai:vision"],
      "outbound_capabilities": []
    }
  ]
}
```

**Bridge health:**
```http
GET /api/v1/bridges/{garden}/health

Response 200:
{
  "garden": "gpu-farm",
  "status": "healthy",
  "latency": {
    "p50_ms": 48,
    "p99_ms": 62
  },
  "uptime_seconds": 1234567,
  "last_heartbeat": "2026-01-20T16:00:02Z",
  "requests_today": 1247,
  "errors_today": 3
}
```

**Update policy:**
```http
PATCH /api/v1/bridges/{garden}/policy
Content-Type: application/json

{
  "outbound_add": ["cache:*"],
  "totp": "482910"
}

Response 200:
{
  "garden": "gpu-farm",
  "policy_updated": true,
  "new_outbound": ["database:*", "storage:*", "cache:*"]
}
```

**Burn bridge:**
```http
DELETE /api/v1/bridges/{garden}
Content-Type: application/json

{
  "totp": "192837"
}

Response 200:
{
  "garden": "gpu-farm",
  "status": "burned",
  "message": "Bridge to gpu-farm has been severed"
}
```

### Federated Resolution

**Resolve capability (includes federated):**
```http
GET /api/v1/resolve/nomic-embed-text

Response 200:
{
  "offering": "nomic-embed-text",
  "capability": "ai:embeddings",
  "source": "federated",
  "garden": "gpu-farm",
  "connection_string": "federated://gpu-farm/ollama/nomic-embed-text?token=eyJ...",
  "health": "healthy",
  "latency_estimate_ms": 48
}
```

### Desire Ledger

**Get desires:**
```http
GET /api/v1/desires

Response 200:
{
  "offerings": [
    {
      "capability": "ai:embeddings",
      "requested_by": ["semantic-search", "smart-tags"],
      "since": "2026-01-20T10:00:00Z"
    }
  ],
  "bridges": [
    {
      "garden": "gpu-farm",
      "status": "pending",
      "received_at": "2026-01-20T15:27:00Z",
      "wants_from_us": [],
      "offers_to_us": ["ai:*"]
    }
  ],
  "stones": []
}
```

---

## Configuration

### Moss Configuration

```toml
# /etc/zen-garden/moss.toml

[federation]
# Enable federation features
enabled = true

# Listen address for bridge connections
listen = "0.0.0.0:7190"

# Maximum active bridges
max_bridges = 10

# Heartbeat settings
heartbeat_interval_seconds = 30
heartbeat_timeout_seconds = 90

[federation.default_policy]
# Default policy for new bridges (can be overridden per-bridge)
inbound_allow = ["ai:*"]
inbound_deny = []
outbound_allow = []
outbound_deny = ["*"]  # Don't share anything by default
```

### Per-Bridge Policy Files

```yaml
# /etc/zen-garden/bridges/gpu-farm.policy.yaml

bridge:
  garden: gpu-farm
  address: gpu-farm.example.com:7190
  
  established: 2026-01-06T10:00:00Z
  keystone_fingerprint: sha256:def456...
  
  policy:
    inbound:
      allow:
        - category: ai
      deny: []
      requirements:
        - capability: ai.vision
          min_vram_gb: 16
          
    outbound:
      allow: []
      deny:
        - category: "*"
```

### Keystone Storage

Bridge Keystones stored alongside Pond Keystone:

```
/etc/zen-garden/
├── keystone.sealed          # Pond Keystone (local CA)
├── bridges/
│   ├── gpu-farm.cert        # Their public certificate
│   ├── gpu-farm.policy.yaml # Bridge policy
│   ├── backup-site.cert
│   └── backup-site.policy.yaml
```

---

## References

- [Ceremonies Specification](ceremonies.md) — Ceremony lifecycle and coordination
- [Security Specification](../specs/security.md) — Keystone, mTLS, TOTP authentication
- [Discovery Protocol](../specs/discovery.md) — mDNS and service resolution
- [Offerings Specification](../specs/offerings.md) — Capability taxonomy
- [CLI Taxonomy Proposal](cli-taxonomy.md) — Zen vs normative command syntax

---

**Last Updated:** January 2026  
**Status:** Proposal — pending review and implementation
