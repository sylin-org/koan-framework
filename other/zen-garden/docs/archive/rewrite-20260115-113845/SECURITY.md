# Security: When to Add the Pond

**Start simple. Add security when stakes rise.**

---

## Two Modes: Dry Garden vs Pond

### Dry Garden (Default)

**No security layer. Services announce openly. Zero setup friction.**

**Use when:**
- Learning Zen Garden
- Home lab / personal projects
- Development environments
- Physical security strong (locked home/office)
- Data non-sensitive (caches, public info)

**Trust model:**
- Assumes: Physical security, trusted LAN
- Vulnerable to: Network sniffing, stolen Stones, rogue devices on LAN

**Setup time:** 2 minutes per Stone

---

### Pond (Secure Mode)

**Cryptographic binding. Stones belong to THIS garden only.**

**Use when:**
- Production workloads
- Sensitive data (PII, financial, health records)
- Compliance requirements (GDPR, HIPAA, SOC2)
- Physical security uncertain (co-working space, shared office)
- Business applications with customer data

**Protection against:**
- ✅ Stolen Stones (won't serve on other networks)
- ✅ Rogue devices (can't fake garden membership)
- ✅ Network sniffing (traffic encrypted)

**Setup time:** 10 minutes (one-time initialization)

---

## Threat Model & Non-Goals

### What Pond Defends Against

**1. Stolen Stones**
- Stone stolen from office → Thief plugs into their network
- **Without Pond**: Extracts data, services work
- **With Pond**: Stone refuses to serve (invalid garden context)

**2. Rogue Devices**
- Attacker brings fake "MongoDB Stone" to your office
- **Without Pond**: Apps connect, attacker captures credentials
- **With Pond**: Invalid certificate rejected, apps refuse connection

**3. Network Sniffing**
- Passive attacker on same LAN captures packets
- **Without Pond**: Cleartext MongoDB traffic visible
- **With Pond**: TLS-encrypted transport (see Protocol Sketch below)

**4. Unauthorized Network Introduction**
- Employee brings home Stone to "help remote colleague"
- **Without Pond**: Works on any network (dangerous)
- **With Pond**: Stone orphaned outside authorized garden

### What Pond Does NOT Defend Against

**⚠️ Physical Access to Running Stone**
- Root shell access → Game over (extract keys, data)
- Pond protects membership, not local root compromise
- Mitigation: Full disk encryption (FDE), secure boot

**⚠️ Application-Level Security**
- MongoDB auth, PostgreSQL roles → Your responsibility
- Pond secures transport + membership, not app logic

**⚠️ Social Engineering**
- Tricked admin installs malicious Stone → Pond can't detect
- Mitigation: Audit trails, least privilege, binding ceremony

**⚠️ Zero-Day Exploits**
- Unpatched kernel/Docker vulnerabilities
- Mitigation: Regular updates, security scanning

### Physical Security Guarantee

> **Factory reset destroys pebbles (cryptographic identity).**  
> Stolen Stone → Reset → No longer bound to garden → Data inaccessible.

---

## Threat Protection Matrix

| Threat | Dry Garden | Pond |
|--------|-----------|------|
| **Stolen Stone** | ❌ Thief extracts data | ✅ Stone refuses to serve |
| **Rogue Device** | ❌ Fake services work | ✅ Invalid cert rejected |
| **Network Sniffing** | ❌ Traffic visible | ✅ Encrypted (TLS) |
| **Physical Root Access** | ❌ Direct data access | ⚠️ Root access still wins |
| **Application Auth** | ⚠️ App's responsibility | ⚠️ App's responsibility |

**Key insight:** Pond protects **garden membership and transport**. Application security (auth, RBAC) is still your responsibility.

---

## How Pond Works (Simple View)

**Initialization:**
```bash
# Create garden identity
zen-garden init --mode pond --create-root-key

# Bind each Stone
zen-garden bind stone-01 --garden-id <ID>
```

**What happens:**
1. Garden gets unique cryptographic identity
2. Each Stone receives certificate proving membership
3. Services only accept connections from valid members
4. Stolen Stones show red LED (orphaned, won't serve)

**Unbinding:** Requires factory reset (destroys pebble + data)

---

## Protocol Sketch

**Cryptography:** Ed25519 (signing), AES-256-GCM (transport)

**Binding ceremony:**
1. Garden controller generates Ed25519 keypair (root identity)
2. Stone generates Ed25519 keypair (device identity)
3. Controller issues certificate: `sign(stone_pubkey, garden_privkey)`
4. Stone stores certificate ("pebble") in tamper-evident location

**Connection flow:**
1. App queries Lantern: "Who has MongoDB?"
2. Lantern responds with: `{ip, port, cert_fingerprint}`
3. App initiates TLS, validates Stone's certificate against garden root
4. If valid → Proceed; if invalid → Reject

**For full protocol details, see [REFERENCE.md](REFERENCE.md#pond-protocol).**

---

## Migration Path: Dry → Pond

**Start dry, upgrade later without rebuilding.**

```bash
# Enable pond mode
zen-garden init --mode pond --create-root-key

# Bind existing Stones (automated)
for stone in $(zen-garden list-stones); do
  zen-garden bind $stone --garden-id <ID>
done

# Restart services with security
zen-garden restart --all
```

**Migration time:** 5-10 minutes per Stone

---

## Disaster Recovery: Backup Stones

**Problem:** Pond controller dies → all bound Stones orphaned

**Solution:** Offsite Backup Stone

**How it works:**
- Backup Stone bound to same pond
- Receives encrypted snapshots automatically
- Stores pond key shard (quorum recovery)
- Lives elsewhere (parent's house, safe deposit box)

**Connection modes:**
- **Periodic sync:** Bring home quarterly, auto-syncs
- **VPN:** Continuous backup via Tailscale/Cloudflare
- **LoRa mesh:** Long-range radio (5-10 miles, no internet)
- **Cellular:** 4G/5G for remote locations

**Recovery scenarios:**
- House fire → Retrieve Backup Stone → Full restore
- Controller dies → 3 Stones quorum → Rebuild pond
- Single Stone fails → Restore from Backup snapshot

**Setup:**
```bash
# During pond initialization
zen-garden init --mode pond --add-backup-stone

# Prompted: "Where will Backup Stone live?"
# Options: Parent's house (VPN), Safe deposit (periodic), Neighbor (LoRa)
```

---

## Decision Guide

**Choose Dry Garden if:**
- ✅ First time using Zen Garden
- ✅ Home network only
- ✅ Non-sensitive data
- ✅ Want fast experimentation

**Add Pond when:**
- 🔒 Deploying to production
- 🔒 Handling customer data
- 🔒 Need compliance (audits, certifications)
- 🔒 Physical theft is credible risk

**Add Backup Stone when:**
- 💾 Data loss unacceptable
- 💾 Disaster recovery required
- 💾 Offsite storage available

---

## What Pond Does NOT Protect

❌ Root access on running Stone (keys extractable)  
❌ Social engineering (tricking admin into adding malicious Stone)  
❌ Zero-day exploits in services  
❌ Insider threats (authorized malicious users)

**Principle:** Pond handles **perimeter security**. You still need application-level controls.

---

## Performance Impact

| Operation | Dry Garden | Pond | Delta |
|-----------|-----------|------|-------|
| **Discovery** | 50-100ms | 50-100ms | +0ms (mDNS identical) |
| **Connection** | <5ms | <10ms | +2-5ms (TLS handshake) |
| **Throughput** | Wire speed | Wire speed | Negligible (hardware TLS) |

**Key insight:** Security overhead is minimal (<5ms per connection).

---

## Quick Start

**Enable Pond mode:**
```bash
# Install on Lantern
zen-garden init --mode pond --create-root-key

# Bind first Stone
zen-garden bind stone-01

# Verify
curl http://lantern.local/api/pond/status
# {"mode": "secure", "stones": 1, "backup": false}
```

**Check Stone status:**
```bash
# On Stone
systemctl status zen-garden-stone

# LED indicators:
# Green: Bound and healthy
# Red: Orphaned (can't find pond)
# Orange: VPN blocked (too many failures)
```

---

## Technical Details

For implementation specifics, see:
- [REFERENCE.md](REFERENCE.md#pond-cryptography) - Certificate formats, key rotation
- [UNDERSTANDING.md](UNDERSTANDING.md#pond-optional-security-layer) - Conceptual overview
- [HARDWARE.md](HARDWARE.md#backup-stone-builds) - Backup Stone hardware options

---

**Summary:** Start with Dry Garden (fast, simple). Add Pond when stakes rise (production, sensitive data). Add Backup Stone for disaster recovery (house fire, theft, failures).

**Philosophy:** Security is opt-in complexity. Use only what you need.

## Implementation Recommendations (Top 5)

### 1. Require Physical + Software Confirmation for Factory Reset

**Requirement**: `garden-wipe factory-reset` must prompt for physical button press within 30 seconds.

**Rationale**: Prevents remote unbinding attacks via code execution vulnerabilities. Software-only factory reset is RCE target.


