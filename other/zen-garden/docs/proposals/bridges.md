# Zen Garden Federation Specification

**Bridges, Meadows, and community capability sharing**

**Status:** Proposal  
**Date:** January 2026  
**Authors:** Collaborative design session

---

## Table of Contents

1. [Overview](#overview)
2. [The Garden Principle](#the-garden-principle)
3. [Bridges](#bridges)
4. [Meadows](#meadows)
5. [Wishes](#wishes)
6. [Bridge Lifecycle](#bridge-lifecycle)
7. [Meadow Lifecycle](#meadow-lifecycle)
8. [Policies](#policies)
9. [Resolution Across Federation](#resolution-across-federation)
10. [Health Monitoring](#health-monitoring)
11. [Security Model](#security-model)
12. [CLI Reference](#cli-reference)
13. [API Specification](#api-specification)
14. [Configuration](#configuration)

---

## Overview

### Federation Hierarchy

Zen Garden provides two levels of federation:

| Level | Name | Relationship | Use Case |
|-------|------|--------------|----------|
| Bilateral | **Bridge** | Garden ↔ Garden | Connect to a friend |
| Multilateral | **Meadow** | Garden ↔ Shared Space ↔ Gardens | Community resource pool |

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│                         MEADOW                                  │
│                     ∴  ∴  ∴  ∴  ∴                              │
│                   ∴              ∴                              │
│      Maria's    ∴                  ∴    João's                  │
│      Garden    ∴      shared        ∴   Garden                  │
│       🌿      ∴     capabilities     ∴    🌿                    │
│        │     ∴                        ∴                         │
│        │      ∴    ai:*, storage:*   ∴                          │
│     BRIDGE     ∴                    ∴                           │
│        │        ∴                  ∴                            │
│        ▼         ∴  ∴  ∴  ∴  ∴  ∴                              │
│    Ana's Garden                          Pedro's Garden         │
│       🌿          (not in meadow)           🌿                  │
│                                       (borders meadow)          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

Maria has a **bridge** to Ana (bilateral). Maria also borders the **meadow** where João and Pedro share capabilities (multilateral).

### What is a Bridge?

A **Bridge** is a secure, policy-controlled connection between two Zen Gardens. Apps in one garden can discover and use offerings from the bridged garden as if they were local.

- **Bilateral** — Two gardens, explicit agreement
- **Intentional** — Built with ceremony, requires effort
- **Point-to-point** — Direct connection

### What is a Meadow?

A **Meadow** is a shared space that multiple gardens can border. Gardens voluntarily contribute capabilities to the meadow, and any garden in the meadow can use them.

- **Multilateral** — Many gardens, community agreement
- **Voluntary** — Each garden chooses what to share
- **Open space** — Shared pool of capabilities

### Wishes

**Wishes** are unfulfilled wants — capabilities that apps crave but the garden cannot yet provide. The wish ledger tracks:

- Offerings apps wish for
- Bridge requests from other gardens
- Meadow invitations
- Stones requesting admission

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

## Bridges

### Why Bridges Exist

Your home lab has mongodb, redis, minio. Your friend has a 48GB VRAM GPU running ollama. Your app wants semantic search — it *wishes* for AI embeddings. Your garden can't provide it. Your friend's garden can.

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

### Bridges vs Ponds

Both provide secure connections. Different scope.

| Aspect | Pond | Bridge |
|--------|------|--------|
| **Scope** | Within a garden | Between gardens |
| **Participants** | Stones | Gardens (via Keystones) |
| **Trust model** | Full trust (same admin) | Partial trust (policy-filtered) |
| **Setup** | `garden-rake place keystone` | `garden-rake bridge with` |
| **Data flow** | Unrestricted within garden | Policy-controlled |
| **Topology** | Visible to all stones | Hidden across bridge |

**Pond = internal security. Bridge = external federation.**

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

---

## Meadows

### Why Meadows Exist

Bridges are bilateral. What if a whole community wants to share?

**The village GPU scenario:**

```
┌─────────────────────────────────────────────────────────────────┐
│                      VILLAGE MEADOW                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Maria's Garden          João's Garden         Ana's Garden    │
│   ┌───────────┐          ┌───────────┐         ┌───────────┐   │
│   │ mongodb   │          │ ollama    │         │ redis     │   │
│   │ postgres  │          │ (GPU)     │         │ minio     │   │
│   └───────────┘          └───────────┘         └───────────┘   │
│        ↓                      ↓                      ↓          │
│        └──────────────────────┼──────────────────────┘          │
│                               ▼                                 │
│                    MEADOW ANNOUNCEMENTS                         │
│              ─────────────────────────────────                  │
│              Maria shares: database:*                           │
│              João shares: ai:* (GPU idle 6pm-8am)              │
│              Ana shares: storage:*                              │
│                                                                 │
│              Anyone in the meadow can use these.                │
│              No bilateral bridges needed.                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

Maria's semantic search app just works. It finds João's GPU through the meadow. João's gaming rig helps the whole village while he sleeps.

### Meadow Characteristics

| Aspect | Bridge | Meadow |
|--------|--------|--------|
| **Relationship** | Bilateral (A ↔ B) | Multilateral (A ↔ Meadow ↔ B, C, D...) |
| **Discovery** | Explicit (I know your address) | Announced (join meadow, see everyone) |
| **Trust** | Point-to-point (I trust you) | Community (I trust the meadow) |
| **Setup** | Per-pair ceremony | One ceremony to join |
| **Governance** | Two parties agree | Community norms, Keepers |

### Keepers

A meadow isn't owned. It's *tended*. **Keepers** are gardeners who help maintain the shared space.

```
Keeper responsibilities:
  • Invite new members (or approve requests)
  • Establish community norms
  • Resolve disputes (rare)
  
Keepers do NOT:
  • Control what members share
  • Have special access to capabilities
  • Own the meadow
```

Multiple Keepers possible. Founding member is first Keeper. Keepership can be passed.

```bash
# Become a keeper
$ garden-rake become keeper of village-tech

# Step down
$ garden-rake step down as keeper of village-tech

# See keepers
$ garden-rake meadow village-tech --keepers
```

### Planting a Meadow

```bash
$ garden-rake plant meadow village-tech

PLANT MEADOW
───────────────────────────────────────────────
  Creating new meadow: village-tech
  
  You will be the founding Keeper.
  
  Meadow philosophy:
    • Members share capabilities voluntarily
    • No member is required to share everything
    • Community sets its own norms
    
  Enter your TOTP to confirm: ______

✓ Meadow "village-tech" planted
✓ You are the founding Keeper

Invite others: garden-rake invite to meadow village-tech
```

### Joining a Meadow

```bash
$ garden-rake join meadow village-tech

JOIN MEADOW
───────────────────────────────────────────────
  Meadow:          village-tech
  Members:         7 gardens
  Shared:          ai:*, database:*, storage:*
  Keepers:         João, Maria
  
  To join, you need an invitation from a Keeper.
  
Request invitation? [Y/n]: y
✓ Request sent to village-tech Keepers
```

Or with an invitation:

```bash
$ garden-rake join meadow village-tech --invitation MDOW-7X9K-M2PL

JOIN MEADOW
───────────────────────────────────────────────
  Meadow:          village-tech
  Invited by:      João
  
  Enter your TOTP to confirm: ______

✓ Your garden now borders the village-tech meadow
✓ You can now access: ai:*, database:*, storage:*
```

### Sharing to a Meadow

```bash
# Share capabilities with the meadow
$ garden-rake share database:* with meadow village-tech

SHARING TO MEADOW
───────────────────────────────────────────────
  Meadow:          village-tech
  Sharing:         database:*
  Your offerings:  mongodb, postgresql
  
  These will be available to all 7 gardens in the meadow.
  
Confirm? [Y/n]: y
✓ Now sharing database:* with village-tech
```

### Time-Based Sharing

```bash
$ garden-rake share ai:* with meadow village-tech --schedule "22:00-06:00"

SCHEDULED SHARING
───────────────────────────────────────────────
  Capabilities:    ai:*
  Meadow:          village-tech
  Schedule:        22:00 - 06:00 daily
  
  Your AI capabilities will be:
    • Available to meadow: 10pm - 6am
    • Reserved for you: 6am - 10pm
    
  Others will see: "ai:embeddings (scheduled 22:00-06:00)"
```

### Leaving a Meadow

```bash
$ garden-rake leave meadow village-tech

LEAVE MEADOW
───────────────────────────────────────────────
  Meadow:          village-tech
  
  This will:
    • Stop sharing your capabilities with the meadow
    • Remove access to meadow capabilities
    • Not affect any direct bridges you have
    
Leave? [Y/n]: y
✓ You have left the village-tech meadow
```

### Reciprocity

Meadows can track contribution vs consumption (visible, not enforced):

```bash
$ garden-rake meadow village-tech --balance

VILLAGE-TECH BALANCE
───────────────────────────────────────────────
  Your garden:     maria-home
  
  CONTRIBUTED (last 30 days)
    database:mongodb        1,247 requests served
    database:postgresql       892 requests served
    
  CONSUMED (last 30 days)
    ai:embeddings            3,891 requests
    storage:backup             127 requests
    
  Standing: Good contributor ✓
  
  Community average: 2.1 (consume/contribute ratio)
  Your ratio: 2.4
```

Social norms, not code restrictions.

---

## Wishes

### Unified View of Unfulfilled Wants

Gardens track **wishes** — things that have been requested but not yet fulfilled:

1. **Offering wishes** — Capabilities apps are wishing for
2. **Bridge wishes** — Other gardens requesting connection
3. **Meadow wishes** — Invitations to join meadows
4. **Stone wishes** — Devices requesting Pond admission

```bash
$ garden-rake wishes

WISHES                                    
───────────────────────────────────────────────
OFFERINGS (apps wish for capabilities)
    ai:embeddings        2 apps         → semantic-search, smart-tags
    ai:vision            1 app          → image-search
    storage:s3           1 app          → cloud-backup

BRIDGES (gardens wish to connect)
    gpu-farm             pending        received 3m ago
                         wants from us: (nothing)
                         offers to us:  ai:*

MEADOWS (invitations)
    village-tech         invited        João invited you
                         members: 7 gardens
                         sharing: ai:*, database:*, storage:*

STONES (devices wish to join pond)
    unknown-device       pending        TOTP: 7X9K2M
                         192.168.1.47
                         4 cores, 8GB RAM
```

### Wish Sources

**Offering wishes** come from apps:
```python
# App registers what it wishes for
garden.wish("ai:embeddings", unlocks="semantic-search")
garden.wish("ai:vision", unlocks="image-search")

# App registers what it requires (hard dependency)
garden.require("mongodb")
```

**Bridge wishes** come from remote gardens:
```bash
# Remote garden initiates
$ garden-rake bridge with home-lab
# This creates a bridge wish in home-lab's ledger
```

**Meadow wishes** come from invitations:
```bash
# Keeper invites a garden
$ garden-rake invite marias-garden to meadow village-tech
# Creates meadow wish in Maria's ledger
```

**Stone wishes** come from devices:
```bash
# New stone requests admission
$ garden-rake place stone
# Creates stone wish in garden's ledger
```

### Fulfilling Wishes

```bash
# Fulfill offering wish by adding capability locally
$ garden-rake offer ollama
✓ ollama planted on stone-02
✓ Fulfilled wishes: ai:embeddings (2 apps), ai:vision (1 app)
  Apps notified of new capabilities

# Fulfill offering wish via bridge
$ garden-rake accept bridge from gpu-farm
✓ Bridge ceremony initiated
✓ Bridge active — ai:* now available via gpu-farm
✓ Fulfilled wishes: ai:embeddings (2 apps), ai:vision (1 app)

# Fulfill offering wish via meadow
$ garden-rake join meadow village-tech
✓ Your garden now borders the village-tech meadow
✓ Fulfilled wishes: ai:embeddings, storage:backup

# Fulfill stone wish
$ garden-rake accept stone 7X9K2M
Enter your TOTP code: ______
✓ unknown-device admitted to pond
✓ Renamed to stone-04
```

### Why Track Wishes?

1. **Visibility** — Know what apps are hungry for
2. **Planning** — Prioritize hardware purchases based on demand
3. **Automation** — Future: auto-accept from trusted sources
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

## Meadow Lifecycle

### States

```
┌─────────────┐
│  PLANTING   │ ← Founder creates the meadow
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   ACTIVE    │ ← Meadow exists, accepting members
└──────┬──────┘
       │
       ├────────────────┐
       ▼                ▼
┌─────────────┐  ┌─────────────┐
│  DORMANT    │  │  DISSOLVED  │
│ (no members)│  │ (explicit)  │
└─────────────┘  └─────────────┘
```

### Planting

```bash
$ garden-rake plant meadow village-tech

PLANT MEADOW
───────────────────────────────────────────────
  Creating: village-tech
  
  You will be the founding Keeper.
  
  Enter your TOTP to confirm: ______

✓ Meadow "village-tech" planted
✓ You are the founding Keeper
✓ Your garden now borders village-tech

Invite others: garden-rake invite <garden> to meadow village-tech
```

### Inviting

Keepers can invite other gardens:

```bash
$ garden-rake invite marias-garden to meadow village-tech

INVITE TO MEADOW
───────────────────────────────────────────────
  Meadow:      village-tech
  Inviting:    marias-garden
  
Generating invitation...
✓ Invitation sent to marias-garden

They will see this in their wishes.
Or share this code directly: MDOW-7X9K-M2PL (expires 7d)
```

### Joining

```bash
$ garden-rake join meadow village-tech --invitation MDOW-7X9K-M2PL

JOIN MEADOW
───────────────────────────────────────────────
  Meadow:      village-tech
  Invited by:  João
  Members:     7 gardens
  Available:   ai:*, database:*, storage:*
  
  Enter your TOTP to confirm: ______

✓ Your garden now borders village-tech
✓ You can access: ai:*, database:*, storage:*

Share your capabilities: garden-rake share <capability> with meadow village-tech
```

### Sharing

```bash
$ garden-rake share database:* with meadow village-tech

SHARE TO MEADOW
───────────────────────────────────────────────
  Meadow:      village-tech
  Sharing:     database:* (mongodb, postgresql)
  
  These offerings will be available to all 8 members.
  
Confirm? [Y/n]: y
✓ Now sharing database:* with village-tech
✓ 8 gardens can now use your databases
```

### Leaving

```bash
$ garden-rake leave meadow village-tech

LEAVE MEADOW
───────────────────────────────────────────────
  Meadow:      village-tech
  
  This will:
    • Stop sharing your capabilities
    • Remove your access to meadow capabilities
    • NOT affect direct bridges
    
  If you're the last Keeper, the meadow will need a new one.
  
Leave? [Y/n]: y
✓ You have left village-tech
```

### Dissolving

The last Keeper can dissolve an empty meadow:

```bash
$ garden-rake dissolve meadow village-tech

DISSOLVE MEADOW
───────────────────────────────────────────────
  Meadow:      village-tech
  Members:     1 (just you)
  
  ⚠ This will permanently end the meadow.
  
  Enter your TOTP to confirm: ______

✓ Meadow village-tech dissolved
```

---

## Policies

### Policy Structure

```yaml
# Bridge policy for connection to gpu-farm
bridge:
  to: gpu-farm
  
  outbound:              # What we SHARE with them
    allow:
      - category: storage
      - category: database
        only: [mongodb]  # Only mongodb, not postgresql
    deny:
      - offering: secrets-manager   # Never share this specific offering
      
  inbound:               # What we ACCEPT from them
    allow:
      - category: ai
      - capability: inference.*
    deny:
      - category: database   # We don't want their databases
    requirements:
      - capability: ai.vision
        min_vram_gb: 16      # Only accept if sufficient VRAM
```

### Policy Evaluation

When an app resolves an offering:

```python
garden.resolve("zen-garden:nomic-embed-text")
```

Resolution checks:
1. **Local first** — Is it available in our garden? → Use local
2. **Bridge check** — Is it available via bridge? 
3. **Policy check** — Does our inbound policy allow it?
4. **Capability check** — Does it meet our requirements?
5. **Return** — Federated endpoint or "not found"

### Policy Negotiation

During bridge ceremony, policies are exchanged and intersected:

```
home-lab wants: ai:*
gpu-farm offers: ai:embeddings, ai:vision, ai:chat

Intersection: ai:embeddings, ai:vision, ai:chat ✓

---

home-lab offers: database:*, storage:*
gpu-farm wants: (nothing)

Intersection: (nothing) ✓
```

If there's no overlap (we want X, they don't offer X), the bridge can still form — it's just one-directional or limited.

### Policy Updates

Policies can be modified on an active bridge:

```bash
$ garden-rake bridge policy gpu-farm --add-inbound "ai:audio"

Updating bridge policy...
Negotiating with gpu-farm...
✓ Policy updated
✓ Now receiving: ai:embeddings, ai:vision, ai:chat, ai:audio
```

The remote garden must agree to share the additional capability.

---

## Resolution Across Federation

### Resolution Priority

Apps use the same connection string format regardless of where the offering lives:

```python
# Local offering
garden.resolve("zen-garden:mongodb")
# Returns: mongodb://stone-01.local:27017

# Federated offering (identical syntax)
garden.resolve("zen-garden:nomic-embed-text")
# Returns: federated://gpu-farm/ollama/nomic-embed-text?token=xyz
```

Resolution order:

```
1. LOCAL GARDEN
   └─ Exact match in local offerings
   
2. DIRECT BRIDGES (by latency)
   └─ Exact match in bridged gardens
   └─ Policy permits
   
3. MEADOWS (by latency)
   └─ Exact match in meadow offerings
   └─ Policy permits
   
4. NOT FOUND
   └─ Offering not available
   └─ Logged as unfulfilled wish
```

### Conflict Resolution

What if multiple sources offer the same capability?

```python
# Both local garden and gpu-farm have "redis"
garden.resolve("zen-garden:redis")
```

**Rule: Local always wins.**

If you have redis locally, you get local redis. Bridges don't override local offerings.

**To explicitly use federated:**

```python
garden.resolve("zen-garden:redis", prefer="federated")
garden.resolve("zen-garden:redis", from_garden="gpu-farm")
```

### Disambiguation

When names conflict across bridges:

```python
# gpu-farm has ollama, ml-cluster also has ollama
garden.resolve("zen-garden:ollama")
# Error: Ambiguous — ollama available from multiple bridges

garden.resolve("zen-garden:ollama", from_garden="gpu-farm")
# Returns: federated://gpu-farm/ollama/...

# Or use capability-based resolution
garden.resolve("zen-garden:ai/embeddings")
# Returns: first available (by latency)
```

### Federated Connection Strings

When resolution returns a federated offering, the connection string includes routing information:

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

## Health Monitoring

### Bridge Health

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
- Apps only see what we allow
- Capability requirements enforced locally

### Threat Model

| Threat | Mitigation |
|--------|------------|
| Rogue garden | TOTP prevents automated bridge requests |
| Man-in-middle | mTLS with certificate pinning |
| Credential theft | Short-lived tokens, Keystone-signed |
| Enumeration | Policies hide non-shared offerings |
| DoS via bridge | Rate limiting on Lantern |
| Capability exfiltration | Outbound policies, audit logging |

### Audit Logging

All bridge activity logged:

```json
{
  "timestamp": "2026-01-20T16:05:23Z",
  "event": "federated_resolution",
  "local_app": "semantic-search",
  "requested": "ai:embeddings",
  "resolved_via": "gpu-farm",
  "offering": "ollama/nomic-embed-text",
  "latency_ms": 52
}
```

---

## CLI Reference

### Bridge Commands (Zen)

```bash
# Request a bridge
garden-rake bridge with <garden-name>
garden-rake bridge with <garden-name> at <address>

# Accept a pending bridge request
garden-rake accept bridge from <garden-name>

# Reject a pending bridge request
garden-rake reject bridge from <garden-name>

# Sever an active bridge
garden-rake burn bridge to <garden-name>

# View all bridges
garden-rake bridges
garden-rake bridges --health

# Inspect a bridge or request
garden-rake inspect bridge <garden-name>

# Modify bridge policy
garden-rake bridge policy <garden-name> --add-inbound "category:*"
garden-rake bridge policy <garden-name> --remove-outbound "offering:secrets"
```

### Meadow Commands (Zen)

```bash
# Plant a new meadow (become founding Keeper)
garden-rake plant meadow <meadow-name>

# Join an existing meadow
garden-rake join meadow <meadow-name>
garden-rake join meadow <meadow-name> --invitation <code>

# Leave a meadow
garden-rake leave meadow <meadow-name>

# View meadow details
garden-rake meadow <meadow-name>
garden-rake meadow <meadow-name> --keepers
garden-rake meadow <meadow-name> --balance

# Share capabilities with meadow
garden-rake share <capability> with meadow <meadow-name>
garden-rake share <capability> with meadow <meadow-name> --schedule "22:00-06:00"

# Stop sharing with meadow
garden-rake unshare <capability> from meadow <meadow-name>

# Invite a garden to meadow (Keeper only)
garden-rake invite <garden-name> to meadow <meadow-name>

# Keeper management
garden-rake become keeper of <meadow-name>
garden-rake step down as keeper of <meadow-name>
```

### Wishes Command (Zen)

```bash
# View all wishes (offerings, bridges, meadows, stones)
garden-rake wishes

# View specific category
garden-rake wishes --offerings
garden-rake wishes --bridges
garden-rake wishes --meadows
garden-rake wishes --stones
```

### Normative Aliases

```bash
# Bridge commands
garden-rake federation connect <garden-name>
garden-rake federation accept <garden-name>
garden-rake federation reject <garden-name>
garden-rake federation disconnect <garden-name>
garden-rake federation list

# Meadow commands
garden-rake community create <meadow-name>
garden-rake community join <meadow-name>
garden-rake community leave <meadow-name>
garden-rake community list

# Wishes
garden-rake requests list
```

---

## API Specification

### Bridge Endpoints (Lantern)

**Request bridge:**
```http
POST /api/v1/bridges/request
Content-Type: application/json

{
  "target_garden": "gpu-farm",
  "target_address": "gpu-farm.example.com:7190",
  "proposed_policy": {
    "we_want": ["ai:*"],
    "we_offer": ["database:*"]
  },
  "totp_proof": "..."
}

Response 202:
{
  "status": "pending",
  "request_id": "bridge-req-abc123",
  "expires_at": "2026-01-21T15:30:00Z",
  "message": "Request sent, awaiting acceptance"
}
```

**Accept bridge:**
```http
POST /api/v1/bridges/accept
Content-Type: application/json

{
  "from_garden": "home-lab",
  "totp_proof": "..."
}

Response 202:
{
  "status": "ceremony_started",
  "ceremony_id": "bridge-homelab-gpufarm-...",
  "message": "Bridge ceremony in progress"
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
      "direction": "inbound",
      "receiving": ["ai:embeddings", "ai:vision"],
      "sharing": [],
      "latency_ms": 48,
      "connected_since": "2026-01-06T10:00:00Z"
    }
  ]
}
```

**Burn bridge:**
```http
DELETE /api/v1/bridges/gpu-farm
Content-Type: application/json

{
  "totp_proof": "..."
}

Response 200:
{
  "status": "burned",
  "garden": "gpu-farm",
  "message": "Bridge severed"
}
```

### Federated Resolution (Lantern)

**Resolve with federation:**
```http
GET /api/v1/resolve?offering=nomic-embed-text&include_federated=true

Response 200:
{
  "offering": "nomic-embed-text",
  "source": "federated",
  "via_garden": "gpu-farm",
  "via_type": "bridge",           // or "meadow"
  "endpoint": "federated://gpu-farm/ollama/nomic-embed-text",
  "token": "eyJ...",
  "token_expires": "2026-01-20T16:10:00Z",
  "latency_estimate_ms": 50
}
```

### Meadow Endpoints (Lantern)

**Plant meadow:**
```http
POST /api/v1/meadows
Content-Type: application/json

{
  "name": "village-tech",
  "totp_proof": "..."
}

Response 201:
{
  "meadow": "village-tech",
  "status": "planted",
  "role": "keeper",
  "message": "Meadow planted. You are the founding Keeper."
}
```

**Join meadow:**
```http
POST /api/v1/meadows/{meadow}/join
Content-Type: application/json

{
  "invitation_code": "MDOW-7X9K-M2PL",  // optional
  "totp_proof": "..."
}

Response 200:
{
  "meadow": "village-tech",
  "status": "joined",
  "role": "member",
  "available_capabilities": ["ai:*", "database:*", "storage:*"]
}
```

**Leave meadow:**
```http
DELETE /api/v1/meadows/{meadow}/membership
Content-Type: application/json

{
  "totp_proof": "..."
}

Response 200:
{
  "meadow": "village-tech",
  "status": "left"
}
```

**Share to meadow:**
```http
POST /api/v1/meadows/{meadow}/share
Content-Type: application/json

{
  "capabilities": ["database:*"],
  "schedule": {
    "start": "22:00",
    "end": "06:00",
    "timezone": "America/New_York"
  }
}

Response 200:
{
  "meadow": "village-tech",
  "sharing": ["database:mongodb", "database:postgresql"],
  "schedule": "22:00-06:00"
}
```

**List meadows:**
```http
GET /api/v1/meadows

Response 200:
{
  "meadows": [
    {
      "name": "village-tech",
      "role": "member",
      "members": 7,
      "sharing": ["database:*"],
      "receiving": ["ai:*", "storage:*"]
    }
  ]
}
```

### Wishes (Moss)

**List wishes:**
```http
GET /api/v1/wishes

Response 200:
{
  "offerings": [
    {"capability": "ai:embeddings", "apps": 2, "unlocks": ["semantic-search"]},
    {"capability": "ai:vision", "apps": 1, "unlocks": ["image-search"]}
  ],
  "bridges": [
    {"garden": "gpu-farm", "status": "pending", "wants": ["ai:*"], "offers": ["database:*"]}
  ],
  "meadows": [
    {"meadow": "village-tech", "status": "invited", "invited_by": "João"}
  ],
  "stones": [
    {"address": "192.168.1.47", "totp_hint": "7X9K2M", "status": "pending"}
  ]
}
```

**Register app wish:**
```http
POST /api/v1/wishes
Content-Type: application/json

{
  "app_id": "semantic-search-app",
  "capability": "ai:embeddings",
  "priority": "desired",
  "unlocks": "semantic-search-feature"
}

Response 201:
{
  "registered": true,
  "capability": "ai:embeddings",
  "status": "unfulfilled",
  "fulfillment_options": [
    "Offer ollama locally",
    "Bridge to a garden with AI capabilities",
    "Join a meadow with AI capabilities"
  ]
}
```

---

## Configuration

### Bridge Configuration

```toml
# /etc/zen-garden/bridges.toml

[federation]
# Enable bridge functionality
enabled = true

# Port for bridge protocol
port = 7190

# Maximum active bridges
max_bridges = 10

# Auto-reconnect settings
reconnect_initial_delay_seconds = 5
reconnect_max_delay_seconds = 600
reconnect_backoff_multiplier = 3.0

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

Bridge and Meadow Keystones stored alongside Pond Keystone:

```
/etc/zen-garden/
├── keystone.sealed          # Pond Keystone (local CA)
├── bridges/
│   ├── gpu-farm.cert        # Their public certificate
│   ├── gpu-farm.policy.yaml # Bridge policy
│   ├── backup-site.cert
│   └── backup-site.policy.yaml
├── meadows/
│   ├── village-tech.membership.yaml  # Membership details
│   └── village-tech.sharing.yaml     # What we share
```

### Meadow Configuration

```toml
# /etc/zen-garden/meadows.toml

[meadows]
# Enable meadow functionality
enabled = true

# Maximum meadows this garden can join
max_meadows = 5

# Default sharing policy for new meadows
default_share = []  # Don't share anything by default

# Heartbeat settings
heartbeat_interval_seconds = 60
```

### Per-Meadow Membership Files

```yaml
# /etc/zen-garden/meadows/village-tech.membership.yaml

meadow:
  name: village-tech
  joined: 2026-01-20T10:00:00Z
  role: member              # or "keeper"
  
  # Our sharing policy for this meadow
  sharing:
    capabilities:
      - database:*
    schedule:
      start: "00:00"
      end: "23:59"
      timezone: "UTC"
      
  # Cached info about the meadow
  keepers:
    - João
    - Maria
  members: 7
  last_sync: 2026-01-20T16:00:00Z
```

---

## References

- [Ceremony Specification](zen-garden-spec-ceremonies.md) — Ceremony lifecycle
- [Security Specification](zen-garden-spec-security.md) — Keystone, mTLS, TOTP
- [Lantern Decision](zen-garden-decisions-all.md#lantern-0001) — Service registry architecture
- [Port Allocation](zen-garden-reference-config.md) — Port 7190 reserved for federation

---

## Glossary

| Term | Meaning |
|------|---------|
| **Bridge** | Bilateral connection between two gardens |
| **Meadow** | Multilateral shared space bordered by many gardens |
| **Keeper** | Caretaker of a meadow (can invite, moderate) |
| **Wishes** | Unfulfilled wants (offerings, bridges, meadows, stones) |
| **Federation** | General term for cross-garden connectivity |

---

**Last Updated:** January 2026  
**Status:** Proposal — pending review and implementation
