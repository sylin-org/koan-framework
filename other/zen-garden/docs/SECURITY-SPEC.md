# Zen Garden Security Specification

**Comprehensive security design for Pond authentication and Stone management**

**Date:** January 15, 2026  
**Status:** Two-tier model finalized  
**Purpose:** Single source of truth for security implementation

---

## Table of Contents

1. [Security Philosophy](#security-philosophy)
2. [Threat Models](#threat-models)
3. [Pond Security Architecture](#pond-security-architecture)
4. [Security Tiers](#security-tiers)
5. [Authentication & Authorization](#authentication--authorization)
6. [Cryptographic Design](#cryptographic-design)
7. [Vulnerability Assessment](#vulnerability-assessment)
8. [Tier 1 Implementation (MVP)](#tier-1-implementation-mvp)
9. [Tier 2 Hardening (Enterprise)](#tier-2-hardening-enterprise)
10. [Operational Security](#operational-security)

---

## Security Philosophy

### Core Principles

**Pragmatic, not paranoid** - Security should protect against realistic threats, not theoretical ones. Home labs face different risks than enterprises.

**Visible, not hidden** - Users should understand security status at a glance. No security through obscurity.

**Recoverable, not fragile** - When issues occur, clear remediation paths. No unrecoverable states.

**Frictionless by default** - Zero configuration for common use cases. Security shouldn't require a PhD.

### Design Tenant

> "Security that requires reading documentation is security that won't be used. Make it visible, understandable, and recoverable."

Every security feature includes three components:

1. **Prevention** (Technical) - Cryptographic/protocol-level security
2. **Detection** (Monitoring) - Visual feedback when issues occur
3. **Recovery** (User-Facing) - Clear steps to fix problems

---

## Threat Models

### Home Lab Reality (Tier 1 Target)

**Environment:**

- Solo admin or small trusted team
- 2-10 Stones on local network
- Physical security assumed (home/office)
- Trusted users (family, colleagues)

**Realistic Threats:**

| Threat           | Likelihood | Impact | Mitigation Priority              |
| ---------------- | ---------- | ------ | -------------------------------- |
| User mistakes    | HIGH       | Medium | **P0** - Safety nets, rollback   |
| Network sniffing | MEDIUM     | Medium | **P0** - Encryption              |
| Physical theft   | LOW        | High   | **P1** - Passphrase + encryption |
| Malware on Stone | LOW        | High   | **P1** - Isolation, monitoring   |
| Insider attack   | VERY LOW   | High   | **P2** - Audit logs              |
| Nation-state     | NEGLIGIBLE | N/A    | Not addressed                    |

**Accepted Risks:**

- Single admin can break things (trust model)
- Pebble extractable with physical access + weak passphrase
- Network partition may cause temporary inconsistency
- Time manipulation possible if router compromised

### Enterprise Reality (Tier 2 Target)

**Environment:**

- Multiple administrators (untrusted)
- 10+ Stones, potentially multi-tenant
- Compliance requirements (GDPR, SOC2, HIPAA)
- Hostile network possible

**Additional Threats:**

| Threat               | Likelihood | Impact   | Mitigation Priority         |
| -------------------- | ---------- | -------- | --------------------------- |
| Insider threat       | HIGH       | Critical | **P0** - Multi-sig, audit   |
| APT attacks          | MEDIUM     | Critical | **P0** - Defense in depth   |
| Lateral movement     | MEDIUM     | Critical | **P0** - Segmentation       |
| Compliance violation | HIGH       | Critical | **P0** - Audit, encryption  |
| Supply chain         | LOW        | Critical | **P1** - Attestation        |
| Data breach          | MEDIUM     | Critical | **P0** - Encryption at rest |

---

## Pond Security Architecture

### Overview

**Pond** - Security model connecting Stones with mutual TLS authentication. Optional, opt-in after initial setup.

**Philosophy:** "Set your stones, make sure everything is working, fill the pond."

Users start without Pond (frictionless), then run `garden-rake place pebble` when ready for security.

### Bluetooth Pairing Model

Inspired by Bluetooth device pairing - familiar UX, proven security model.

**Security team rating: 9.5/10**

#### Analogy

```
Bluetooth Pairing          →  Pond Join
─────────────────────────────────────────
1. Put device in pairing mode  →  garden-rake invite stone
2. Shows 6-digit code          →  Displays TOTP code locally
3. Type code on other device   →  Type code on new Stone
4. Devices paired              →  Stone joined pond
```

### Two-Keypair Architecture

Each Stone maintains two keypairs:

**1. Stone Identity Keypair** (unique per Stone)

```yaml
Private Key: stone-XX.key
Public Key: stone-XX.pub
Purpose:
  - Generate TOTP codes
  - Sign bearer tokens
  - Encrypt sensitive data
Storage: /var/lib/zen-garden/stone-XX.key (encrypted at rest)
```

**2. Pond CA Keypair** (shared across pond)

```yaml
Private Key: pond-ca.key
Public Key: pond-ca.pub
Purpose:
  - Issue certificates to new Stones
  - Validate join requests
  - Sign configuration changes
Storage: Encrypted with each Stone's key (replicated)
Distribution: Automatically replicated to all pond Stones
```

### Two Join Flows

#### Flow 1: Invited Join (User-Initiated)

**Scenario:** User wants to add a specific Stone to the pond.

```
1. User on stone-01: garden-rake invite stone
2. stone-01 generates code: TOTP(stone-01.key, time) = "AJ4R9X"
3. stone-01 displays code locally (never transmitted)
4. User walks to new-stone, types: garden-rake place stone AJ4R9X
5. new-stone broadcasts join request (encrypted with pond CA pubkey)
6. stone-01 handles automatically (no election, user directed)
7. stone-01 ↔ new-stone: Direct peer-to-peer handshake
8. stone-01 validates code, sends encrypted pebble
9. new-stone stores pebble, joins pond
```

**Key feature:** User directs which Stone handles join (no election needed).

#### Flow 2: Spontaneous Join (Stone-Initiated)

**Scenario:** New Stone announces itself (e.g., USB boot on old laptop).

```
1. new-stone boots, announces: "I want to join!"
2. All pond Stones calculate election delay:
   delay = hash(stone_name + new_stone_name + salt) % 5000 ms
3. stone-06 wins (shortest delay)
4. stone-06 generates code: TOTP(stone-06.key, time) = "123456"
5. stone-06 broadcasts notification:
   "Stone 'old-laptop-01' wants to join! Code: 123456"
6. User sees notification (phone/desktop/any Stone)
7. User walks to old-laptop-01, types: 123456
8. old-laptop-01 → stone-06: "I hear you, Stone-06"
9. Other Stones disengage (election complete)
10. stone-06 ↔ old-laptop-01: Direct peer-to-peer handshake
11. stone-06 validates code, sends encrypted pebble
12. old-laptop-01 stores pebble, joins pond
```

**Key features:**

- Distributed election (hash-based staggered delay)
- Per-Stone TOTP secrets (each Stone uses own private key)
- User notification (multiple channels: mDNS, WebSocket, mobile)

### Distributed Election Algorithm

**Why needed:** Prevent network flooding when multiple Stones respond simultaneously.

**Algorithm:**

```rust
// Each Stone calculates own delay independently
fn calculate_election_delay(new_stone: &str, salt: &[u8]) -> u64 {
    let payload = format!("{}:{}:{}",
        my_stone_name(),
        new_stone,
        hex::encode(salt)
    );
    let hash = sha256(payload.as_bytes());

    // Convert first 2 bytes to 0-5000ms delay
    u64::from_be_bytes([hash[0], hash[1], 0, 0, 0, 0, 0, 0]) % 5000
}

// Stone with shortest delay wins
async fn handle_spontaneous_join(new_stone: &str, salt: &[u8]) {
    let my_delay = calculate_election_delay(new_stone, salt);

    sleep(Duration::from_millis(my_delay)).await;

    // Check if another Stone already won
    if join_already_handled(new_stone).await {
        return; // Disengage
    }

    // I won! Generate code and handle join
    generate_and_broadcast_code(new_stone).await;
}
```

**Properties:**

- Deterministic (same inputs = same winner)
- Distributed (no central coordinator)
- Non-predictable (salt prevents pre-calculation)
- Staggered (prevents simultaneous broadcast)

---

## Security Tiers

### Tier Comparison

|                  | Tier 1: Garden Pond    | Tier 2: Fortress Pond  |
| ---------------- | ---------------------- | ---------------------- |
| **Target**       | Home labs, small teams | Enterprise, compliance |
| **Complexity**   | Low (zero config)      | High (TPM, quorum)     |
| **Effort**       | 1 week                 | 8 weeks                |
| **Security**     | 7/10                   | 9.5/10                 |
| **Threat Model** | Accidents > attacks    | Insider threats, APT   |
| **Philosophy**   | Frictionless           | Defense in depth       |

---

## Tier 1: Garden Pond (Default)

### Target Audience

- Solo administrator or small trusted team
- 2-10 Stones on local network
- Home lab, development environment
- Physical security assumed

### Philosophy

**Protect against accidents, not nation-states.**

Focus on preventing user mistakes, providing clear feedback, and enabling easy recovery. Security should be invisible when working, obvious when broken.

### Security Posture: 7/10

Excellent for home labs. Adequate for small trusted teams. Not suitable for untrusted networks or compliance requirements.

### Implemented Security (P0)

**1. Configuration Safety Net**

```
Prevention: Warn before security config changes
Detection: Show recent changes in status
Recovery: One-command rollback
```

**2. Encrypted Join Requests**

```
Prevention: Ed25519 encryption with Pond CA pubkey
Detection: Invalid encryption triggers error
Recovery: Retry with NTP sync
```

**3. Short-Lived Certificates**

```
Prevention: 1-hour validity, auto-renew every 30 min
Detection: Certificate expiration warnings
Recovery: Auto-renewal, manual renew command
```

**4. Certificate CN Binding**

```
Prevention: Extract identity from mTLS, ignore headers
Detection: Mismatch triggers alert + audit log
Recovery: Investigation workflow
```

**5. Absolute Timestamp Validation**

```
Prevention: Reject codes older than 10 minutes
Detection: Clock drift warnings
Recovery: Auto-sync via NTP or Cornerstone
```

**6. Used Codes Tracking**

```
Prevention: SQLite persistence, broadcast on use
Detection: Duplicate code attempt logged
Recovery: Request new code
```

**7. Visual Security Feedback**

```
Prevention: Status command shows all health
Detection: Warnings displayed proactively
Recovery: Actionable remediation steps
```

### Simplified Approach (vs Enterprise)

**Time Synchronization:**

- Single NTP source (pool.ntp.org)
- Cornerstone fallback if NTP unavailable
- Wide time window (±10 min) to reduce false rejections
- No NTP consensus required

**Election:**

- User-directed for invited joins (no election)
- Hash-based staggered delay for spontaneous joins
- Smart defaults (least-loaded Stone preferred)
- 10-second user override timeout

**Partition Handling:**

- Auto-detected via heartbeat
- Visual warning displayed
- Read-only mode during partition
- Auto-recovery when healed
- No quorum voting needed

**Pebble Security:**

- Strong passphrase required (20+ chars)
- Argon2id key derivation
- Encrypted at rest (AES-256-GCM)
- No TPM/HSM mandatory (recommended for P1)

**Rate Limiting:**

- Simple MAC-based (5 attempts per hour)
- Manual unban: `garden-rake unban <MAC>`
- No advanced fingerprinting required

**Audit Logs:**

- SQLite local storage
- Signed entries (tamper-evident)
- No distributed replication mandatory
- Retention: 30 days configurable

### Accepted Risks

**1. Single Admin Trust Model**

```
Risk: Admin can change any security configuration
Impact: Could weaken security posture
Mitigation: Safety net (warn → confirm → audit → rollback)
Rationale: Home lab = trusted admin, not insider threat
```

**2. Pebble Extractable with Physical Access**

```
Risk: Stolen Stone + weak passphrase = pebble extracted
Impact: Attacker can join pond, issue certificates
Mitigation: Passphrase entropy check (20+ chars), zxcvbn meter
Rationale: Home burglary unlikely, physical security assumed
```

**3. Network Partition Inconsistency**

```
Risk: Split-brain during partition (rare)
Impact: Temporary state inconsistency
Mitigation: Visual warning, read-only mode, manual reconciliation
Rationale: Home network partition rare and short-lived
```

**4. Time Oracle Attack**

```
Risk: Attacker controls home router, manipulates NTP
Impact: Could bypass time-based security
Mitigation: Visual time drift warnings, multi-source NTP (P1)
Rationale: Home router compromise unlikely
```

### Implementation Burden

```yaml
Complexity: Low
- Configuration: None (sane defaults)
- Maintenance: Zero (auto-healing)
- User Education: Minimal (visual feedback)

Development Effort:
- Day 1: Configuration safety net
- Day 2: Short-lived certificates + auto-renew
- Day 3: Certificate CN binding
- Day 4: Absolute timestamp validation
- Day 5: Visual security feedback
Total: 1 week to production-ready
```

### Commands

```bash
# Initialize pond (Tier 1 automatically)
garden-rake place pebble

# Invite Stone to join
garden-rake invite stone

# Join pond
garden-rake place stone AJ4R9X

# Configuration management
garden-rake config set bearer_token_ttl short
garden-rake config history
garden-rake config rollback

# Security status
garden-rake status
garden-rake status --time
garden-rake status --certs

# Rate limiting
garden-rake ban <MAC>
garden-rake unban <MAC>
```

---

## Tier 2: Fortress Pond (Enterprise)

### Target Audience

- Multiple administrators (untrusted)
- 10+ Stones, potentially multi-tenant
- Compliance requirements (GDPR, SOC2, HIPAA)
- Untrusted network possible

### Philosophy

**Defense in depth. Assume breach. Regulatory compliance.**

Every layer hardened. Insider threats addressed. Comprehensive audit trails. Zero-trust architecture.

### Security Posture: 9.5/10

Production enterprise ready. Suitable for regulated industries. Compliant with major standards.

### Additional Hardening (Beyond Tier 1)

**1. Multi-Signature Configuration**

```
Prevention: 2-of-3 admin approval for security configs
Detection: Unapproved changes rejected
Recovery: Revoke admin keys, rotate CA
```

**2. Cryptographic Election**

```
Prevention: Challenge-response with nonce signatures
Detection: Invalid signatures rejected
Recovery: Blacklist malicious Stones
```

**3. NTP Consensus**

```
Prevention: Query 3+ sources, require majority agreement
Detection: Outlier rejection (> 5 sec deviation)
Recovery: Alert on time sync failures
```

**4. Hardware Security**

```
Prevention: TPM 2.0 / Secure Enclave mandatory
Detection: Missing TPM blocks pond creation
Recovery: Provision TPM, import sealed keys
```

**5. Advanced Monitoring**

```
Prevention: SIEM integration, real-time anomaly detection
Detection: Behavioral analysis, threat intelligence
Recovery: Automated incident response, isolation
```

**6. Partition Resilience**

```
Prevention: Raft/Paxos consensus for joins
Detection: Merkle tree state reconciliation
Recovery: Automatic conflict resolution
```

**7. Compliance Features**

```
Prevention: Encryption at rest (FIPS 140-2)
Detection: Compliance violation alerts
Recovery: Right-to-forget, data purging
```

**8. Device Fingerprinting**

```
Prevention: MAC + IP + TLS fingerprint + hostname
Detection: Spoofing attempts detected
Recovery: Quarantine suspicious devices
```

**9. Distributed Audit Logs**

```
Prevention: Blockchain-style chaining, replication
Detection: Tampering breaks signature chain
Recovery: Forensic analysis, incident reports
```

### Implementation Burden

```yaml
Complexity: High
- Configuration: Complex (quorum, HSM, SIEM)
- Maintenance: Ongoing (cert rotation, monitoring)
- User Education: Extensive (admin training, runbooks)

Development Effort:
- Week 1-3: Core hardening (multi-sig, NTP, TPM)
- Week 4-5: Compliance features (audit, encryption)
- Week 6-7: Testing (security audit, penetration test)
- Week 8: Documentation (runbooks, compliance docs)
Total: 8 weeks for enterprise-grade security
```

### Activation

```bash
# Enable enterprise hardening (explicit opt-in)
garden-rake harden pond

⚠️  FORTRESS MODE: Enterprise Security Hardening

This will enable:
  • Multi-signature configuration approvals
  • Hardware security module (TPM) requirement
  • NTP consensus time validation
  • Advanced audit logging
  • Partition quorum enforcement

Requirements:
  ✓ 3+ Stones (for quorum voting)
  ✗ TPM 2.0 detected on all Stones (2/4 missing)
  ✓ Admin key pairs generated (3 admins configured)

Cannot enable Fortress Mode:
  Reason: TPM 2.0 required but not available

Options:
  1. Install TPM modules on stone-02, stone-04
  2. Use software TPM (not recommended): --software-tpm
  3. Skip TPM requirement (degrades security): --no-tpm
```

---

## Authentication & Authorization

### TOTP Code Generation

**Algorithm:** HMAC-based One-Time Password (RFC 6238)

```rust
fn generate_code(stone_key: &[u8], timestamp: u64) -> String {
    // Time window: 5 minutes (300 seconds)
    let time_window = timestamp / 300;

    // HMAC-SHA256 with Stone private key
    let payload = format!("{}:{}", hex::encode(stone_key), time_window);
    let hmac = hmac_sha256(stone_key, payload.as_bytes());

    // Extract 20 bits (1M+ combinations)
    let code_bits = u32::from_be_bytes([hmac[0], hmac[1], hmac[2], 0]) >> 12;

    // Encode as 6-character base36 (A-Z, 0-9)
    encode_base36(code_bits)
}
```

**Properties:**

- 6 characters: 1,048,576 combinations
- 5-minute TTL
- ±10 min window (Tier 1) or ±5 min (Tier 2)
- Never transmitted over network
- Validated with Stone private key

### Bearer Token Design

**Purpose:** Authenticate inter-Stone API calls after join.

**Format:** JWT with custom claims

```json
{
  "stone_name": "stone-02",
  "operation": "offer_service",
  "timestamp": 1705334500,
  "nonce": "7f3a9b2c1d8e4f6a",
  "expires_at": 1705334800
}
```

**Signature:** HMAC-SHA256(payload, stone_private_key)

**TTL:** Configurable (short/progressive/long)

**Short Mode (default, 5 min):**

```
Use case: Most operations (offer, remove, status)
Security: Minimal replay window
Suitable for: Standard network conditions
```

**Progressive Mode:**

```
Use case: Different TTLs by operation type
- Read (status, list): 30 seconds
- Write (offer, remove): 5 minutes
- Admin (rotate, ban): 10 minutes
Security: Optimized per operation
Suitable for: Advanced users
```

**Long Mode (1 hour):**

```
Use case: Slow networks, large image pulls
Security: Requires nonce tracking (prevents replay)
Suitable for: Reliability > security environments
```

### mTLS Certificates

**Lifetime:** 1 hour (Tier 1), 30 minutes (Tier 2)

**Auto-Renewal:** Every 30 minutes (50% lifetime)

**Revocation:** Stop issuing renewals (max 1 hour until invalid)

**CN Binding:** Certificate Common Name = stone-XX (trusted source)

```rust
fn issue_certificate(stone_name: &str, pubkey: &PublicKey) -> Certificate {
    Certificate::new()
        .common_name(stone_name)
        .subject_alt_names(vec![
            format!("{}.local", stone_name),
            format!("{}.pond", stone_name),
        ])
        .validity_duration(Duration::hours(1))
        .sign_with(pond_ca_key())
}
```

### Rate Limiting

**Tier 1: MAC-Based**

```
Limit: 5 join attempts per MAC address per hour
Storage: SQLite local + broadcast
Ban duration: 1 hour (configurable)
Unban: Manual via CLI (garden-rake unban <MAC>)
```

**Tier 2: Device Fingerprinting**

```
Fingerprint: MAC + IP + TLS fingerprint + hostname
Limit: 3 attempts per fingerprint per hour
Storage: Distributed (replicated across Stones)
Ban duration: Escalating (1h → 6h → 24h)
Unban: Multi-admin approval required
```

---

## Cryptographic Design

### Key Management

**Stone Identity Keypair:**

```yaml
Algorithm: Ed25519 (elliptic curve)
Key Size: 256-bit
Generation: On first boot (stone init)
Storage: /var/lib/zen-garden/stone-XX.key
Encryption: AES-256-GCM with passphrase-derived key
KDF: Argon2id (memory: 64MB, iterations: 3)
Purpose: Sign bearer tokens, generate TOTP codes
```

**Pond CA Keypair:**

```yaml
Algorithm: Ed25519
Key Size: 256-bit
Generation: On pond initialization (place pebble)
Storage: Encrypted with each Stone's public key (replicated)
Distribution: Automatic to all pond Stones
Purpose: Issue certificates, validate join requests
Rotation: Manual (garden-rake rotate pond-ca)
```

### Encryption at Rest

**Pebble File (pond-ca.key):**

```
Algorithm: AES-256-GCM
Key Derivation: Argon2id
  - Memory: 64MB (Tier 1), 256MB (Tier 2)
  - Iterations: 3 (Tier 1), 10 (Tier 2)
  - Parallelism: 2 threads
Passphrase Entropy: 20+ characters required
Storage: /var/lib/zen-garden/pebble.enc
```

**Used Codes Database:**

```
File: /var/lib/zen-garden/used_codes.db
Encryption: SQLite with SQLCipher extension
Key: Derived from Stone private key
Schema: (code TEXT, stone_name TEXT, used_at INTEGER)
Retention: 24 hours (configurable)
```

### Encryption in Transit

**Join Request:**

```
Algorithm: Hybrid encryption (Ed25519 + AES-256-GCM)
Process:
  1. Generate ephemeral AES key
  2. Encrypt request payload with AES
  3. Encrypt AES key with Pond CA pubkey (Ed25519)
  4. Send: encrypted_payload + encrypted_key
Result: Forward secrecy, quantum-resistant (post-quantum upgrade path)
```

**Bearer Tokens:**

```
Format: JWT (header.payload.signature)
Signature: HMAC-SHA256(header + payload, stone_private_key)
Validation: Verify signature with Stone public key
Transmission: HTTPS only (mTLS enforced in pond mode)
```

### Passphrase Requirements

**Tier 1:**

```
Minimum Length: 20 characters
Strength Check: zxcvbn library (score ≥ 3/4)
Generated Option: 6-word diceware passphrases offered
Enforcement: Reject weak passphrases, show strength meter
Example: "correct-horse-battery-staple-garden-zenith"
```

**Tier 2:**

```
Minimum Length: 24 characters
Strength Check: zxcvbn (score = 4/4 required)
Generated Option: 8-word diceware mandatory for critical operations
Rotation: Every 90 days
MFA: Hardware token (YubiKey) or biometric required
```

---

## Vulnerability Assessment

### Summary Matrix

| Vulnerability              | Tier 1    | Tier 2      | Status                        |
| -------------------------- | --------- | ----------- | ----------------------------- |
| Config propagation exploit | ⚠️ MEDIUM | 🔴 CRITICAL | P0 Fix: Safety net            |
| Predictable election       | 🟢 LOW    | 🟡 HIGH     | P0 Fix: Random salt           |
| Time oracle attack         | 🟢 LOW    | 🔴 CRITICAL | P0 Fix: Timestamp validation  |
| MAC spoofing               | 🟡 MEDIUM | 🟡 HIGH     | P1 Fix: Fingerprinting        |
| Missing cert revocation    | 🟡 HIGH   | 🔴 CRITICAL | P0 Fix: Short-lived certs     |
| Pebble dictionary attack   | 🟡 MEDIUM | 🔴 CRITICAL | P1 Fix: Passphrase entropy    |
| Used codes desync          | 🟡 MEDIUM | 🟡 HIGH     | P0 Fix: Timestamp + broadcast |
| Stone impersonation        | 🟡 HIGH   | 🔴 CRITICAL | P0 Fix: CN binding            |
| Join flood DoS             | 🟢 LOW    | 🟡 MEDIUM   | P1 Fix: Rate limiting         |
| Split-brain                | 🟢 LOW    | 🟡 HIGH     | P1 Fix: Partition detection   |
| Audit log tampering        | 🟢 LOW    | 🔴 CRITICAL | P1 Fix: Signed logs           |
| Bearer token replay        | 🟢 LOW    | 🟡 MEDIUM   | P0 Fix: Nonce tracking        |
| mDNS flooding              | 🟢 LOW    | 🟢 LOW      | P2 Fix: Signature filtering   |

### Detailed Vulnerabilities

Full vulnerability analysis with attack scenarios, impact assessments, and mitigations documented in original SECURITY-TEAM-REVISED-ASSESSMENT.md (lines 1100-2200). Key highlights:

**13 vulnerabilities identified:**

- 3 CRITICAL (reclassified to LOW/MEDIUM for Tier 1)
- 5 HIGH (addressed with P0 fixes)
- 3 MEDIUM (scheduled for P1)
- 2 LOW (acceptable residual risk)

**Reclassification rationale:**
Home lab threat model differs significantly from enterprise. Vulnerabilities rated CRITICAL for enterprises become LOW/MEDIUM for home labs due to:

- Trusted admin model (no insider threat)
- Physical security assumed
- Simplified recovery acceptable
- Manual intervention feasible

---

## Tier 1 Implementation (MVP)

### P0 Security Fixes (1 Week)

**Day 1: Configuration Safety Net**

```rust
const SECURITY_CONFIGS: &[&str] = &[
    "bearer_token_ttl",
    "rate_limit_attempts",
    "code_ttl_minutes",
];

fn update_config(key: &str, value: &str) -> Result<()> {
    if SECURITY_CONFIGS.contains(&key) {
        // Visual warning
        eprintln!("⚠️  Changing security configuration: {}", key);
        eprintln!("   Current: {}", config::get(key)?);
        eprintln!("   New: {}", value);
        eprintln!("   Impact: All {} Stones", pond_stone_count()?);

        // Confirmation (unless --yes)
        if !confirm("Apply pond-wide?")? {
            return Ok(());
        }

        // Audit log
        audit_log(ConfigChanged { key, value })?;
    }

    config::set(key, value)?;
    broadcast_config_update(key, value)?;
    Ok(())
}
```

**Day 2: Short-Lived Certificates + Auto-Renewal**

```rust
fn issue_certificate(stone: &str, pubkey: &PublicKey) -> Certificate {
    Certificate::new()
        .common_name(stone)
        .validity_duration(Duration::hours(1))
        .sign_with(pond_ca_key())
}

async fn auto_renew_loop() {
    loop {
        sleep(Duration::minutes(30)).await;

        if cert_expires_in() < Duration::minutes(45) {
            renew_certificate().await?;
        }
    }
}
```

**Day 3: Certificate CN Binding**

```rust
fn verify_request(req: &Request, tls: &TlsStream) -> Result<Identity> {
    let cert = tls.peer_certificate()?;
    let cert_cn = cert.subject_common_name()?;
    let claimed = req.headers().get("X-Stone-Name")?;

    if claimed != cert_cn {
        audit_log(IdentityMismatch { claimed, cert_cn })?;
        alert_user(SuspiciousActivity)?;
        return Err(IdentityMismatch);
    }

    Ok(Identity { name: cert_cn, verified: true })
}
```

**Day 4: Absolute Timestamp Validation**

```rust
fn validate_code(code: &str, timestamp: u64) -> Result<()> {
    let now = current_time();
    let age = now - timestamp;

    // Reject codes older than 10 minutes (absolute)
    if age > 600 {
        return Err(CodeExpired(age));
    }

    // Check time windows (±10 min for Tier 1)
    for offset in -2..=2 {
        let window = (timestamp / 300) + offset;
        if check_code_for_window(code, window)? {
            return Ok(());
        }
    }

    Err(InvalidCode)
}
```

**Day 5: Visual Security Feedback**

```bash
$ garden-rake status

Stone: stone-01 (Cornerstone)
Status: ✓ Healthy
Pond: My Garden (4 Stones)

Security:
  ✓ Certificate valid (expires in 42 min, auto-renewing)
  ✓ Time synchronized (NTP: pool.ntp.org, drift < 1 sec)
  ✓ Configuration healthy (no recent changes)
  ⚠️ stone-03 offline (last seen 5 min ago)

Services (2):
  ✓ mongodb (healthy, 450 MB)
  ✓ redis (healthy, 80 MB)
```

### Validation Checklist

- [ ] User can misconfigure and rollback easily
- [ ] Compromised Stone loses access in < 1 hour
- [ ] Stone impersonation prevented
- [ ] Network partition doesn't break joins
- [ ] User always knows security posture
- [ ] Zero configuration required (sane defaults)
- [ ] Visual feedback at every step

---

## Tier 2 Hardening (Enterprise)

### Additional Features (8 Weeks)

**Week 1-2: Multi-Signature Configuration**

- Quorum voting (2/3 majority)
- Admin key management
- Proposal/approval workflow
- Emergency override (3/5 admins)

**Week 3: NTP Consensus**

- Query 3+ sources (pool.ntp.org, time.google.com, time.cloudflare.com)
- Median time calculation
- Outlier rejection (> 5 sec deviation)
- RFC 8915 (Rough Time Protocol) support

**Week 4: Hardware Security**

- TPM 2.0 integration (tpm2-tss library)
- Secure Enclave (macOS/iOS)
- Hardware key storage
- Sealed pebble (cannot extract)

**Week 5: Advanced Monitoring**

- SIEM integration (syslog, Splunk, ELK)
- Real-time anomaly detection
- Behavioral analysis
- Automated incident response

**Week 6: Partition Resilience**

- Raft consensus for joins
- Merkle tree state reconciliation
- Automatic conflict resolution
- Byzantine fault tolerance (3f+1 nodes)

**Week 7: Compliance Features**

- GDPR: Right-to-forget, data portability
- SOC2: Audit trails, access logs
- HIPAA: Encryption at rest, PHI handling
- PCI-DSS: Key rotation, audit requirements

**Week 8: Testing & Documentation**

- Security audit (external firm)
- Penetration testing
- Compliance documentation
- Admin training materials

---

## Operational Security

### Time Synchronization

**Tier 1: Simple NTP + Cornerstone Fallback**

```bash
# Automatic (on join)
garden-rake place stone AJ4R9X
→ Checks NTP sync automatically
→ Falls back to Cornerstone if NTP unavailable
→ Wide window (±10 min) reduces false rejections

# Manual (if needed)
garden-rake sync-time
→ Tries pool.ntp.org
→ Falls back to Cornerstone
→ Shows time sources and drift
```

**Tier 2: NTP Consensus**

```rust
async fn sync_time_consensus() -> Result<SystemTime> {
    let servers = ["pool.ntp.org", "time.google.com", "time.cloudflare.com"];
    let mut times = Vec::new();

    for server in servers {
        if let Ok(time) = query_ntp(server).await {
            times.push(time);
        }
    }

    if times.len() < 2 {
        return Err(NtpSyncFailed);
    }

    times.sort();
    let median = times[times.len() / 2];

    // Reject outliers
    for time in &times {
        if time.duration_since(median)?.as_secs() > 5 {
            return Err(NtpOutlierDetected);
        }
    }

    Ok(median)
}
```

### Partition Handling

**Tier 1: Visual Warning + Read-Only**

```
Detection: Heartbeat every 30 sec
Threshold: < 50% Stones visible
Action: Enter read-only mode
Display: Visual warning in all commands
Recovery: Automatic when partition heals
```

**Tier 2: Quorum-Based Operations**

```
Detection: Raft consensus layer
Threshold: 2/3 Stones required
Action: Block writes to minority partition
Display: Partition status in monitoring
Recovery: Merkle tree reconciliation on heal
```

### Certificate Revocation

**Tier 1: Stop Renewals**

```bash
garden-rake revoke stone-03 --reason "Suspected compromise"

✓ Stone revoked: stone-03
  Certificate expires in: 45 minutes (automatic)
  Access blocked pond-wide immediately

# Revocation broadcast to all Stones
# Compromised Stone loses access in < 1 hour
```

**Tier 2: Certificate Revocation List (CRL)**

```rust
// Cornerstone maintains CRL
fn add_to_crl(stone: &str, serial: &str) -> Result<()> {
    db::execute(
        "INSERT INTO crl (stone, serial, revoked_at) VALUES (?, ?, ?)",
        (stone, serial, now())
    )?;

    broadcast_crl_update(stone, serial)?;
    Ok(())
}

// All Stones check CRL before accepting connections
fn verify_certificate(cert: &Certificate) -> Result<()> {
    let serial = cert.serial_number()?;

    if is_revoked(serial)? {
        return Err(CertificateRevoked);
    }

    Ok(())
}
```

### Audit Logging

**Tier 1: Signed Local Logs**

```rust
// Append-only log with signature chaining
struct AuditEntry {
    event: AuditEvent,
    timestamp: u64,
    stone: String,
    prev_hash: [u8; 32],
    signature: Vec<u8>,
}

fn audit_log(event: AuditEvent) -> Result<()> {
    let prev_hash = last_entry_hash()?;
    let entry = AuditEntry {
        event,
        timestamp: now(),
        stone: my_stone_name(),
        prev_hash,
        signature: sign(payload, my_key()),
    };

    append_to_log(entry)?;
    Ok(())
}
```

**Tier 2: Distributed Replication**

```rust
// Broadcast audit events to all Stones
fn audit_log_distributed(event: AuditEvent) -> Result<()> {
    let entry = create_audit_entry(event)?;

    // Local storage
    append_to_log(entry.clone())?;

    // Replicate to all Stones
    broadcast_audit_entry(entry)?;

    // Wait for 2/3 acknowledgments
    wait_for_quorum_ack(entry.id).await?;

    Ok(())
}
```

### Incident Response

**Tier 1: Manual Investigation**

```bash
# Audit trail
garden-rake audit --filter identity_mismatch
garden-rake audit --filter config_changed

# Investigation
garden-rake investigate stone-03

# Revocation
garden-rake revoke stone-03 --reason "Compromise confirmed"
```

**Tier 2: Automated Response**

```
Detection: SIEM alerts (anomaly detected)
Isolation: Automatic quarantine (block all connections)
Investigation: Forensic snapshot captured
Notification: Admin alert (email/SMS/PagerDuty)
Remediation: Guided recovery workflow
Post-Incident: Automated report generation
```

---

## Recommended MVP (Tier 1 Only)

### Scope

**For Zen Garden 1.0 release:**

Implement Tier 1 (Garden Pond) only. Defer Tier 2 (Fortress Pond) to 2.0 based on user feedback and enterprise demand.

### Rationale

**Target Audience:**

- Home labs (90% of initial users)
- Small teams (< 10 people)
- Development environments
- Non-compliance use cases

**UX Priority:**

- Frictionless setup (zero configuration)
- Simple mental model (Bluetooth pairing)
- Clear visual feedback (always know status)
- Easy recovery (rollback, sync, renew)

**Complexity Budget:**

- 1 week implementation (not 2 months)
- Zero maintenance overhead
- No complex dependencies (TPM, HSM, SIEM)
- Simple documentation (no training required)

**Risk Appetite:**

- Accept home lab threat model
- Trust admin (solo or small team)
- Physical security assumed
- Manual reconciliation acceptable

### Benefits

✅ **Ship in 1 week vs 8 weeks**  
✅ **Simple, understandable security**  
✅ **No complex configuration**  
✅ **Clear upgrade path for enterprises**  
✅ **Validate with real users before enterprise features**

### Trade-Offs

⚠️ **Not suitable for regulated industries (initially)**  
⚠️ **Single admin trust model (acceptable for home)**  
⚠️ **Manual reconciliation on edge cases (rare)**  
⚠️ **No compliance certifications (add later)**

---

## Commands Reference

### Pond Management

```bash
# Initialize pond (Tier 1)
garden-rake place pebble

# Join pond (invited)
garden-rake invite stone      # On inviting Stone
garden-rake place stone CODE  # On new Stone

# Security status
garden-rake status
garden-rake status --time
garden-rake status --certs

# Configuration
garden-rake config list
garden-rake config get bearer_token_ttl
garden-rake config set bearer_token_ttl short
garden-rake config history
garden-rake config rollback

# Rate limiting
garden-rake ban <MAC> --reason "Brute force attempt"
garden-rake unban <MAC>

# Time sync
garden-rake sync-time
garden-rake sync-time --all

# Certificate management
garden-rake cert renew
garden-rake cert status
garden-rake revoke stone-03 --reason "Compromised"

# Audit
garden-rake audit
garden-rake audit --filter identity_mismatch
garden-rake investigate stone-03

# Enterprise hardening (Tier 2)
garden-rake harden pond
```

---

## Future Work (Tier 2)

**Post-1.0 based on user demand:**

- Multi-signature configuration approvals
- TPM/Secure Enclave integration
- NTP consensus (3+ sources)
- Quorum-based operations
- Advanced audit logging (SIEM)
- Compliance certifications (GDPR, SOC2, HIPAA)
- Device fingerprinting (spoofing prevention)
- Proof-of-work (DoS prevention)
- Partition resilience (Raft/Paxos)
- Behavioral anomaly detection

---

## References

### Internal Documentation

- [TECHNICAL-SPEC.md](TECHNICAL-SPEC.md) - Moss/Rake implementation
- [UNDERSTANDING.md](UNDERSTANDING.md) - Core concepts
- [STORIES.md](STORIES.md) - User scenarios

### Standards & RFCs

- RFC 6238: TOTP (Time-Based One-Time Password)
- RFC 6762: mDNS (Multicast DNS)
- RFC 8915: Rough Time Protocol
- NIST FIPS 140-2: Cryptographic Module Security

### Cryptography

- Ed25519: EdDSA signature scheme
- AES-256-GCM: Authenticated encryption
- Argon2id: Memory-hard KDF
- HMAC-SHA256: Message authentication

---

**Document Status:** Two-tier model finalized, ready for Tier 1 implementation

**Last Updated:** January 15, 2026

**Security Team Rating:**

- Tier 1 (Garden Pond): 7/10 - Excellent for home labs
- Tier 2 (Fortress Pond): 9.5/10 - Production enterprise ready

**Contributors:** Security team (Cryptography, Threat Model, IAM, OpSec), Development team
