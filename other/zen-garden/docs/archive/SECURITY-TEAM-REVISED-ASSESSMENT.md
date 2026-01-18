# Security Team: Revised Invitation Protocol Assessment

**Date:** January 15, 2026  
**Topic:** Time-Based Codes + Asymmetric Encryption  
**Status:** Review of revised proposal

---

## Revised Proposal Summary

**User's proposal:**

1. Code calculated locally using `pebble + time` (TOTP-style)
2. Code NEVER transmitted over network (displayed locally only)
3. TTL 5 minutes (±5 min drift tolerance)
4. Once used, code is banned
5. "Cornerstone" = first Stone with pond (master CA)
6. `place stone` encrypts code with cornerstone's public key
7. Cornerstone validates with ±5 min time drift

---

## Security Team Assessment

### Cryptography Architect: 9/10 ⭐⭐⭐

**Verdict: EXCELLENT improvement**

#### Strengths

✅ **Zero network exposure** - Code never leaves cornerstone screen  
✅ **TOTP-proven design** - Same algorithm as Google Authenticator, battle-tested  
✅ **Asymmetric encryption** - Prevents network sniffing of join request  
✅ **Time-bound** - Auto-expiration without server state  
✅ **Deterministic** - No need to pre-register codes

#### Technical Analysis

**TOTP Implementation:**

```rust
// Cornerstone generates code
fn generate_code(pebble_secret: &[u8], time_window: u64) -> String {
    let payload = format!("{}:{}", hex::encode(pebble_secret), time_window);
    let hmac = hmac_sha256(pebble_secret, payload.as_bytes());
    let code_bits = u32::from_be_bytes([hmac[0], hmac[1], hmac[2], 0]) >> 12;
    encode_base36(code_bits) // 20-bit = 6 chars = ~1M combinations
}

// New Stone encrypts request
fn encrypt_join_request(code: &str, cornerstone_pubkey: &PublicKey) -> Vec<u8> {
    let payload = serde_json::json!({
        "code": code,
        "stone_name": hostname(),
        "stone_pubkey": my_pubkey(),
        "timestamp": current_time()
    });
    cornerstone_pubkey.encrypt(payload.to_string().as_bytes())
}

// Cornerstone validates
fn validate_join(encrypted: &[u8], cornerstone_keypair: &KeyPair) -> Result<()> {
    let decrypted = cornerstone_keypair.decrypt(encrypted)?;
    let request: JoinRequest = serde_json::from_slice(&decrypted)?;

    // Check time windows: current, -5min, +5min
    for offset in [-1, 0, 1] {
        let window = (now() / 300) + offset;
        let expected = generate_code(pebble_secret(), window);
        if request.code == expected {
            if !is_code_used(&request.code)? {
                mark_code_used(&request.code, &request.stone_name)?;
                return Ok(());
            }
        }
    }

    Err(InvalidCode)
}
```

#### Remaining Concerns (Minor)

**1. Clock Synchronization (HIGH importance)**

```
Problem: New Stone clock drift > 5 min → code invalid

Mitigation:
- Check NTP before join attempt
- Display clock drift warning to user
- Suggest: sudo ntpdate pool.ntp.org
```

**2. Cornerstone Availability (MEDIUM importance)**

```
Problem: Cornerstone offline → cannot add Stones

Mitigation:
- Cornerstone replica: Any pond Stone can validate codes
- Replicate pebble_secret to all pond Stones (encrypted)
- mDNS flag: cornerstone=true|replica
```

**3. Used Codes Persistence (MEDIUM importance)**

```
Problem: Cornerstone restart → used codes forgotten → replay attack

Mitigation:
- Store used codes in SQLite: /var/lib/zen-garden/used_codes.db
- Schema: (code TEXT, stone_name TEXT, used_at INTEGER, PRIMARY KEY(code))
- Cleanup old codes (> 24 hours) on startup
```

**4. Bearer Token TTL (HIGH importance)**

```
Problem: Inter-Stone commands need authentication (garden-rake offer mongodb --at stone-02)

Solution: Configurable TTL with three profiles

Profile 1: Short (5 minutes, default)
  - Covers most operations (offer, remove, status)
  - Image pull: 2-5 min typical
  - Minimal replay window
  - Recommended for security-conscious environments

Profile 2: Progressive (by operation type)
  - Read operations (status, list): 30 seconds
  - Write operations (offer, remove): 5 minutes
  - Admin operations (rotate, ban, unban): 10 minutes
  - Optimized per operation, smallest replay window
  - Recommended for advanced users

Profile 3: Long (1 hour)
  - Covers edge cases (slow networks, large images)
  - Requires nonce tracking (prevent replay)
  - Recommended for reliability > security environments

Configuration:
  garden-rake config set bearer_token_ttl short|progressive|long

Changes propagate to all pond Stones automatically via mDNS broadcast

Implementation:
  - Token payload: {stone_name, operation, timestamp, nonce}
  - Signature: HMAC-SHA256(payload, stone_private_key)
  - Validation: Check timestamp + TTL, verify signature, check nonce (long mode)
  - Nonce storage: In-memory cache (1h retention) + SQLite persistence
```

**5. Brute Force (LOW importance)**

```
Problem: Attacker with physical access to cornerstone tries codes

Mitigation:
- 6-character code = 1M+ combinations
- Rate limit: 10 attempts per IP per 5 min
- Exponential backoff after 3 failures
- Audit log all attempts

Calculation:
- 10 attempts / 5 min = 2 attempts/min
- 1M combinations / 2 attempts/min = 500K minutes = 347 days
(Impractical even with physical access)
```

#### Recommendations

**P0:**

1. ✅ Use Ed25519 for cornerstone keypair (small, fast)
2. ✅ Encrypt join request with cornerstone pubkey
3. ✅ Validate code with ±5 min window (3 time slots)
4. ✅ Persist used codes in SQLite
5. ✅ Check NTP before join (warn on drift > 5 min)

**P1:**

1. ✅ Cornerstone replica (all pond Stones can validate)
2. ✅ Rate limiting (10 attempts per IP per 5 min)
3. ✅ Audit log (all join attempts, success/failure)
4. ✅ Cleanup old used codes (retention: 24 hours)
5. ✅ Bearer token TTL (configurable: short/progressive/long, default: short)

#### Configuration Propagation

**Mechanism:**

```rust
// Configuration change broadcast
fn update_config(key: &str, value: &str) -> Result<()> {
    // Update local config
    config::set(key, value)?;

    // Broadcast to all pond Stones
    let payload = serde_json::json!({
        "event": "config_updated",
        "key": key,
        "value": value,
        "stone": my_stone_name(),
        "timestamp": current_time()
    });

    let signature = sign(payload.to_string(), my_private_key());

    mdns_broadcast("_koan-stone._tcp.local.", payload, signature)?;

    Ok(())
}

// Configuration receiver
fn handle_config_broadcast(payload: &str, signature: &str) -> Result<()> {
    let config: ConfigUpdate = serde_json::from_str(payload)?;

    // Verify signature (from trusted pond Stone)
    verify_signature(payload, signature, config.stone)?;

    // Apply configuration
    config::set(&config.key, &config.value)?;

    println!("✓ Configuration updated: {} = {} (from {})",
        config.key, config.value, config.stone);

    Ok(())
}
```

**Supported Configuration Keys:**

```yaml
bearers_token_ttl: short|progressive|long
  Description: Token validity duration
  Default: short
  Impact: All inter-Stone API calls

rate_limit_attempts: 1-100
  Description: Max join attempts per MAC per hour
  Default: 5
  Impact: Join request throttling

rate_limit_ban_duration: 1-24 (hours)
  Description: Ban duration after exceeding rate limit
  Default: 1
  Impact: MAC address banning

code_ttl_minutes: 1-30
  Description: Invitation code validity window
  Default: 5
  Impact: Join code expiration

used_codes_retention_hours: 1-168 (1 week)
  Description: Used codes cleanup window
  Default: 24
  Impact: SQLite database size

notification_channels: mdns,websocket,mobile (comma-separated)
  Description: Enabled notification channels
  Default: mdns,websocket
  Impact: Spontaneous join notifications
```

**Configuration Commands:**

```bash
# View current configuration
garden-rake config list
garden-rake config get bearer_token_ttl

# Update configuration (propagates to all Stones)
garden-rake config set bearer_token_ttl progressive
garden-rake config set rate_limit_attempts 10

# Reset to defaults
garden-rake config reset bearer_token_ttl
garden-rake config reset --all
```

---

### Threat Model Analyst: 9.5/10 ⭐⭐⭐

**Verdict: OUTSTANDING - Addresses all attack vectors**

#### Attack Scenarios (Re-evaluated)

**Scenario 1: Network Eavesdropping**

```
Before: Code transmitted in HTTP request
Attack: Sniff network, capture code, join rogue Stone

After: Code encrypted with cornerstone pubkey
Result: Attacker sees: <binary blob>, cannot extract code
Verdict: ✅ MITIGATED
```

**Scenario 2: Replay Attack**

```
Before: Capture join request, replay later
Attack: Re-send captured request to join multiple Stones

After: Used codes tracked in database, one-time use
Result: Second attempt returns "Code already used"
Verdict: ✅ MITIGATED
```

**Scenario 3: Man-in-the-Middle**

```
Before: Attacker intercepts request, modifies stone_name
Attack: Join as different Stone name

After: Entire request encrypted with cornerstone pubkey
Result: Attacker cannot modify encrypted payload
Verdict: ✅ MITIGATED
```

**Scenario 4: Time Manipulation**

```
Attack: Attacker sets clock forward/backward to generate future/past codes

Mitigation:
- New Stone checks NTP before join
- Cornerstone validates timestamp in request
- Only ±5 min window accepted

Verdict: ⚠️ PARTIALLY MITIGATED (depends on NTP availability)
```

**Scenario 5: Cornerstone Compromise**

```
Attack: Attacker gains access to cornerstone, extracts pebble_secret

Impact:
- Attacker can calculate all future codes
- Attacker can join rogue Stones

Mitigation:
- Pebble encrypted at rest (AES-256-GCM with passphrase)
- Require physical access + passphrase to extract
- Revoke cornerstone if compromised (generate new pebble)

Verdict: ⚠️ ACCEPTABLE (requires physical access + passphrase)
```

#### Threat Matrix

| Attack Vector          | Before         | After                       | Status     |
| ---------------------- | -------------- | --------------------------- | ---------- |
| Network sniffing       | ❌ Vulnerable  | ✅ Encrypted                | MITIGATED  |
| Replay attack          | ❌ Vulnerable  | ✅ One-time use             | MITIGATED  |
| MITM                   | ❌ Vulnerable  | ✅ Encrypted                | MITIGATED  |
| Brute force            | ❌ 65K combos  | ✅ 1M+ combos + rate limit  | MITIGATED  |
| Time manipulation      | ⚠️ Possible    | ⚠️ NTP check                | PARTIAL    |
| Cornerstone compromise | ⚠️ High impact | ⚠️ Requires physical + pass | ACCEPTABLE |

#### Recommendations

**P0:**

1. ✅ NTP check before join (mandatory, not optional)
2. ✅ Validate timestamp in join request (reject if > 5 min drift)
3. ✅ Encrypt pebble at rest with passphrase-derived key

**P1:**

1. ✅ Hardware security module for cornerstone pebble (TPM, Secure Enclave)
2. ✅ Cornerstone rotation (`garden-rake rotate cornerstone stone-02`)
3. ✅ Alert on failed join attempts (email/webhook)

---

### Identity & Access Management: 8.5/10 ⭐⭐

**Verdict: STRONG - Cornerstone concept excellent**

#### Access Control Model

**Cornerstone Role:**

```yaml
Cornerstone (first Stone with pond):
  Permissions:
    - Generate invitation codes (display locally)
    - Validate join requests (decrypt, verify TOTP)
    - Issue certificates to new Stones
    - Track used codes
    - Broadcast stone joined events

  Trust Model:
    - Cornerstone = root of trust
    - All certificates signed by cornerstone CA key
    - Compromise cornerstone = compromise entire pond

  High-Value Target:
    - Pebble contains CA private key
    - Used codes database enables replay detection
    - Must be physically secured
```

**Cornerstone Replica Model (Recommended):**

```yaml
Replica Model:
  - All pond Stones receive encrypted copy of pebble
  - Any pond Stone can validate invitations
  - Load balanced (round-robin or health-based)
  - Single point of failure eliminated

  Security:
    - Pebble replicated with master key (derived from passphrase)
    - Each Stone decrypts with local passphrase cache
    - Replica Stones cannot extract pebble (encrypted at rest)

  Degradation:
    - If majority of pond Stones offline → invite fails
    - Quorum: Require 1+ Stone online (simple)
    - Advanced: Require 2/3 majority (Byzantine fault tolerance)
```

#### Recommendations

**P0:**

1. ✅ Cornerstone replica (all pond Stones can validate)
2. ✅ mDNS flag: cornerstone=true|replica
3. ✅ Encrypt replicated pebbles with master key

**P1:**

1. ✅ Quorum model (2/3 Stones must agree for join)
2. ✅ Cornerstone rotation (transfer CA authority to another Stone)
3. ✅ Multi-signature invitations (require 2+ admins to approve)

---

### Operational Security: 9/10 ⭐⭐⭐

**Verdict: EXCELLENT - Solves most operational issues**

#### Operational Benefits

**1. No Pre-Registration**

```
Before: garden-rake invite stone → store code in database
Problem: Database must persist, survive restarts

After: garden-rake invite stone → calculate code, display
Benefit: No state management, deterministic, always works
```

**2. Auto-Expiration**

```
Before: Manually expire codes after 5 minutes (cron job?)
Problem: Complexity, race conditions

After: Time-based validation (±5 min window)
Benefit: Auto-expires, no cleanup needed
```

**3. Offline Capability**

```
Before: Cornerstone must be reachable to generate code
Problem: Network partition → cannot add Stones

After: Any pond Stone can calculate code (if replica)
Benefit: High availability, no single point of failure
```

#### Operational Concerns

**1. NTP Dependency**

```
Problem: New Stone without network time sync → join fails

Mitigation:
- Attempt NTP sync before join (pool.ntp.org)
- If NTP fails, warn user and suggest manual sync
- Allow manual clock adjustment in join flow

User flow:
$ garden-rake place stone AJ4R9X
⚠ Warning: Clock drift detected (7 minutes behind)
  Attempting NTP sync... ✓ (synchronized with pool.ntp.org)
  Retrying join... ✓
```

**2. Used Codes Storage**

```
Problem: SQLite file grows unbounded, eventual disk full

Mitigation:
- Cleanup codes older than 24 hours (startup + daily cron)
- Codes only valid for 5 min, 24h retention = 287x safety margin
- Estimate: 1000 joins/day = 365K codes/year = ~10MB storage

Schema:
CREATE TABLE used_codes (
  code TEXT PRIMARY KEY,
  stone_name TEXT NOT NULL,
  used_at INTEGER NOT NULL,  -- Unix timestamp
  INDEX idx_used_at (used_at)
);

Cleanup:
DELETE FROM used_codes WHERE used_at < (unixepoch() - 86400);
VACUUM;
```

**3. Cornerstone Disaster Recovery**

```
Problem: Cornerstone hardware failure → cannot add Stones

Mitigation:
- Export pebble during pond init:
  garden-rake export pebble --output pebble-backup.enc

- Restore on new cornerstone:
  garden-rake import pebble --input pebble-backup.enc

- Replica model eliminates this concern (any Stone can act as cornerstone)
```

#### Recommendations

**P0:**

1. ✅ NTP sync check + auto-retry
2. ✅ Used codes cleanup (daily, keep 24h)
3. ✅ Cornerstone replica (eliminate single point of failure)

**P1:**

1. ✅ Pebble backup/restore commands
2. ✅ Health check (detect clock drift > 5 min)
3. ✅ Monitoring (alert on failed join attempts)

---

### Developer Experience: 9.5/10 ⭐⭐⭐

**Verdict: OUTSTANDING UX - User never sees complexity**

#### User Experience

**Invitation Flow:**

```bash
# On cornerstone (any pond Stone)
$ garden-rake invite stone

Checking clock synchronization... ✓ (NTP: pool.ntp.org)
Generating invitation code... ✓

┌─────────────────────────────────────────────────┐
│ Invitation Code: AJ4R9X                         │
│                                                 │
│ Valid for: 5 minutes                            │
│ Expires at: 12:35:00 UTC                        │
│                                                 │
│ On new Stone, run:                              │
│   garden-rake place stone AJ4R9X                │
│                                                 │
│ ⓘ Code is time-based and auto-expires          │
│   No need to manually revoke                    │
└─────────────────────────────────────────────────┘

Waiting for Stone to join... (Ctrl+C to cancel)
```

**Join Flow:**

```bash
# On new Stone
$ garden-rake place stone AJ4R9X

Checking clock synchronization...
  ⚠ Clock drift detected: 7 minutes behind
  Attempting NTP sync... ✓ (synchronized)

Discovering cornerstone...
  ✓ Found: stone-01.local (192.168.1.10)

Joining pond...
  [1/4] Generating key pair... ✓
  [2/4] Encrypting join request... ✓
  [3/4] Requesting certificate... ✓
  [4/4] Installing pebble... ✓

✓ Joined pond successfully

Stone: stone-04
Pond: My Secure Garden
Certificate expires: 2027-01-15
Cornerstone: stone-01.local

Next steps:
  - Check status: garden-rake status
  - Install service: garden-rake offer mongodb
```

**Error Handling:**

```bash
# Error: Clock drift
$ garden-rake place stone AJ4R9X

✗ Error: Clock drift too large (12 minutes behind)

Your system clock is out of sync. To fix:

  Option 1 (automatic):
    garden-rake place stone AJ4R9X --sync-clock

  Option 2 (manual):
    sudo ntpdate pool.ntp.org
    garden-rake place stone AJ4R9X

  Option 3 (skip check, not recommended):
    garden-rake place stone AJ4R9X --force

# Error: Code expired
$ garden-rake place stone AJ4R9X

✗ Error: Invitation code expired or invalid

Possible reasons:
  - Code expired (valid for 5 minutes only)
  - Clock drift (check: date)
  - Already used by another Stone

Request new code from pond admin:
  garden-rake invite stone

# Error: Code already used
$ garden-rake place stone AJ4R9X

✗ Error: Invitation code already used

This code was used by: stone-03 (2 minutes ago)

Request new code from pond admin:
  garden-rake invite stone
```

#### User Benefits

✅ **Simple mental model** - "Show me a code, I type it"  
✅ **No network concepts** - User doesn't think about encryption  
✅ **Self-healing** - NTP sync automatic, clock drift handled  
✅ **Clear errors** - Always explains what went wrong + how to fix  
✅ **No manual cleanup** - Codes auto-expire, no revoke needed

---

## Team Consensus (REVISED)

### Overall Rating: 9/10 ⭐⭐⭐ (Upgraded from 8/10)

**Verdict: SHIP IT** (with P0 items)

### Critical Improvements ✅

1. ✅ **Network sniffing eliminated** - Code never transmitted
2. ✅ **Replay attacks prevented** - One-time use tracking
3. ✅ **MITM attacks prevented** - Asymmetric encryption
4. ✅ **Auto-expiration** - Time-based, no manual cleanup
5. ✅ **Deterministic** - No pre-registration, always works

### Critical Improvements (Final) ✅

1. ✅ **Two join flows** - Invited (user-initiated) + Spontaneous (USB boot)
2. ✅ **Distributed election** - Hash-based staggered delay prevents network rush
3. ✅ **Per-Stone TOTP secrets** - Each Stone uses own private key for code generation
4. ✅ **Bluetooth pairing model** - Familiar UX, security team rating: 9.5/10
5. ✅ **Bearer token TTL** - Configurable (short/progressive/long), default: short (5 min)
6. ✅ **Configuration propagation** - Changes broadcast to all Stones automatically
7. ✅ **Notification channels** - All three enabled (mDNS, WebSocket, mobile)
8. ✅ **Rate limiting** - MAC-based, ban/unban commands available

### Remaining Concerns (Minor) ⚠️

1. **NTP dependency** (P0) - Must sync clocks before join
2. **Cornerstone replica** (P1) - Eliminate single point of failure
3. **Used codes persistence** (P0) - SQLite with configurable cleanup
4. **6-character codes** (P0) - Increase from 4 to 6 chars
5. **Nonce tracking** (P1) - Prevent bearer token replay (long TTL mode)

### Implementation Checklist

**Phase 1 (MVP):**

- [x] TOTP code generation (pebble_secret + time)
- [x] Asymmetric encryption (Ed25519 keypair)
- [x] Join request encryption (cornerstone pubkey)
- [x] Code validation (±5 min window)
- [x] Used codes tracking (SQLite)
- [x] NTP sync check (mandatory)
- [x] 6-character codes (20-bit entropy)
- [x] Two join flows (invited + spontaneous)
- [x] Distributed election (hash-based staggered delay)
- [x] Bearer token TTL (configurable: short/progressive/long)
- [x] Configuration propagation (mDNS broadcast)
- [x] Notification channels (mDNS, WebSocket, mobile)
- [x] Rate limiting (MAC-based, max 5 per hour)

**Phase 2 (Production-Ready):**

- [ ] Cornerstone replica (all pond Stones can validate)
- [ ] Audit logging (all join attempts)
- [ ] Used codes cleanup (daily cron, configurable retention)
- [ ] Health monitoring (clock drift alerts)
- [ ] WebSocket notification channel (desktop clients)
- [ ] Nonce tracking (bearer token replay prevention)

**Phase 3 (Advanced):**

- [ ] Hardware security module (TPM, Secure Enclave)
- [ ] Quorum model (2/3 majority for join approval)
- [ ] Multi-signature invitations (2+ admins)
- [ ] Cornerstone rotation (transfer CA authority)

---

## Revised Command Specification

### Invite Stone (Updated)

```bash
garden-rake invite stone [--expires <minutes>] [--show-details]

Options:
  --expires <minutes>    Code validity window (default: 5, max: 30)
  --show-details         Show cryptographic details (debug mode)

Example:
  garden-rake invite stone

Output:
  ┌─────────────────────────────────────────────────┐
  │ Invitation Code: AJ4R9X                         │
  │ Valid for: 5 minutes                            │
  │ Expires at: 12:35:00 UTC                        │
  │ On new Stone: garden-rake place stone AJ4R9X    │
  └─────────────────────────────────────────────────┘

Security:
  - Code calculated from pebble secret + current time (TOTP)
  - Never transmitted over network
  - Valid for ±5 minutes (3 time windows)
  - One-time use (tracked in database)
  - Automatically expires
```

### Join Pond (Updated)

```bash
garden-rake place stone <code> [--sync-clock] [--force]

Arguments:
  <code>   6-character invitation code

Options:
  --sync-clock   Automatically sync clock via NTP if drift detected
  --force        Skip clock sync check (not recommended)

Example:
  garden-rake place stone AJ4R9X

Process:
  1. Check clock synchronization (±5 min tolerance)
  2. Discover cornerstone via mDNS
  3. Generate Ed25519 key pair
  4. Encrypt join request with cornerstone public key
  5. Send encrypted request to cornerstone
  6. Receive and verify certificate
  7. Store pebble (encrypted)
  8. Update mDNS announcement

Security:
  - Request encrypted with cornerstone public key
  - Code validated with ±5 min time window
  - One-time use enforced
  - Certificate issued by cornerstone CA
```

---

## Open Questions (Updated)

### Resolved ✅

1. **Bearer token TTL** - Configurable (short 5min / progressive / long 1h), default: short
2. **Notification channels** - All three enabled (mDNS, WebSocket, mobile)
3. **User approval timeout** - 5 minutes (matches code TTL)
4. **Rate limiting** - MAC-based, max 5 per hour, unban command available
5. **Configuration propagation** - Broadcast via mDNS, all Stones update automatically

### Remaining 🔄

1. **Cornerstone replica distribution:**
   - Should pebble auto-replicate to all pond Stones?
   - Require manual replication (`garden-rake replicate pebble`)?
   - Risk: More Stones with pebble = more attack surface
   - **Recommendation:** Auto-replicate to all Stones, encrypted with each Stone's key

2. **Clock synchronization:**
   - Require NTP sync (strict) or allow manual override?
   - Which NTP servers? (pool.ntp.org? custom?)
   - Fallback if NTP unavailable?
   - **Recommendation:** pool.ntp.org primary, allow custom, mandatory sync check

3. **Used codes retention:**
   - 24 hours sufficient or too long?
   - SQLite vs in-memory (performance vs persistence)
   - Distributed used-codes tracking (all Stones share)?
   - **Recommendation:** 24h SQLite with broadcast, configurable via config

4. **Code length:**
   - 6 characters (1M combos) or 8 characters (2.8B combos)?
   - User experience vs security trade-off
   - **Recommendation:** 6 characters (balance UX + security)

5. **Cornerstone rotation:**
   - Automatic (primary cornerstone offline → replica promoted)?
   - Manual only (`garden-rake promote stone-02`)?
   - Election algorithm (Raft? Paxos? Simple quorum?)
   - **Recommendation:** Manual only for MVP, auto-promotion in P2

6. **Configuration versioning:**
   - How to handle config conflicts (two Stones change same key)?
   - Last-write-wins? Vector clocks? Conflict resolution UI?
   - **Recommendation:** Last-write-wins + audit log for MVP

---

## Final Recommendation (Updated)

**Verdict: 9/10 - EXCELLENT design, SHIP with P0 items**

The revised proposal with **time-based codes** and **asymmetric encryption** is a **significant security improvement** over the original. It eliminates network exposure, prevents replay attacks, and provides auto-expiration without manual cleanup.

### Must-Have (P0):

1. ✅ TOTP code generation (pebble + time)
2. ✅ Asymmetric encryption (Ed25519)
3. ✅ Used codes persistence (SQLite)
4. ✅ NTP sync check (mandatory)
5. ✅ 6-character codes (not 4)
6. ✅ Rate limiting (10 attempts per IP)

### Should-Have (P1):

1. ✅ Cornerstone replica (high availability)
2. ✅ Audit logging (security events)
3. ✅ Health monitoring (clock drift alerts)
4. ✅ Cleanup automation (daily cron)

### Nice-to-Have (P2):

1. ⚪ Hardware security module (TPM)
2. ⚪ Quorum model (2/3 majority)
3. ⚪ Multi-signature invitations

**Team approves: Proceed with implementation** 🚀

---

# COMPREHENSIVE SECURITY AUDIT

**Date:** January 15, 2026  
**Scope:** Full architecture review - adversarial perspective  
**Objective:** Identify exploitable vulnerabilities before production

---

## Red Team Analysis: Critical Vulnerabilities

### 🔴 CRITICAL: Configuration Propagation Exploit

**Attack Vector:** Compromised Stone broadcasts malicious configuration

```rust
// Attacker on compromised stone-03
garden-rake config set bearer_token_ttl long   // Extend replay window
garden-rake config set rate_limit_attempts 100  // Disable throttling
garden-rake config set code_ttl_minutes 30      // Longer code validity

// Result: ALL pond Stones apply malicious config
// Attacker now has 30 min codes, 1h bearer tokens, no rate limiting
```

**Impact:** CRITICAL - Single compromised Stone controls entire pond security posture

**Mitigations:**

```yaml
Option 1: Configuration ACL (recommended)
  - Only Cornerstone can change security-critical configs
  - Other Stones can request, Cornerstone approves
  - Commands: garden-rake config request <key> <value>

Option 2: Quorum-based config changes
  - Require 2/3 majority approval for security configs
  - Broadcast vote request, wait for responses
  - Prevents single compromised Stone attack

Option 3: Configuration signatures
  - Config changes must be signed by multiple admin keys
  - Require 2-of-3 or 3-of-5 multi-sig
  - Store admin public keys in pebble
```

**Recommendation:** P0 - Implement Option 1 (ACL) for MVP, Option 2 for production

---

### 🔴 CRITICAL: Predictable Election Algorithm

**Attack Vector:** Attacker calculates election winner, targets specific Stone

```python
# Attacker code
def predict_election_winner(new_stone_name: str, pond_stones: list) -> str:
    delays = {}
    for stone in pond_stones:
        hash_input = f"{stone}{new_stone_name}"
        delay = hashlib.sha256(hash_input.encode()).digest()[0:2]
        delays[stone] = int.from_bytes(delay, 'big') % 5000

    return min(delays, key=delays.get)

# Attacker discovers pond Stones via mDNS
stones = ["stone-01", "stone-02", "stone-03", "stone-04"]

# Try join request with predictable name
winner = predict_election_winner("attacker-stone", stones)
print(f"stone-{winner} will handle join - prepare targeted attack")

# Attacker: DoS stone-03 during join, race condition exploit
```

**Impact:** HIGH - Attacker can target weakest/slowest Stone in pond

**Mitigations:**

```yaml
Option 1: Non-deterministic delay (recommended)
  - Add random salt: hash(stone_name + new_stone_name + random_salt)
  - New Stone includes random salt in join request
  - Unpredictable, attacker cannot pre-calculate

Option 2: Challenge-response election
  - New Stone broadcasts join request with nonce
  - All Stones respond with: sign(nonce, stone_key)
  - New Stone picks random valid responder
  - Attacker cannot predict without knowing Stone private keys

Option 3: Round-robin with state
  - Track last Stone that handled join
  - Next join goes to next Stone in list
  - Predictable but load-balanced, requires state sync
```

**Recommendation:** P0 - Implement Option 1 (random salt)

---

### 🔴 CRITICAL: Time Oracle Attack

**Attack Vector:** Attacker controls NTP server, manipulates Stone clocks

```bash
# Attacker sets up rogue NTP server
# Poisons ARP cache: pool.ntp.org -> attacker IP

# Scenario 1: Code replay
# Set new Stone clock 10 min forward
# User generates code: AJ4R9X (time_window = 1705334500)
# Attacker captures encrypted join request (contains code)
# Set new Stone clock 10 min backward (time_window = 1705333900)
# Code now valid for another 10 minutes
# Attacker replays join request with rogue Stone

# Scenario 2: Bearer token extension
# Compromise stone-03, steal bearer token
# Set stone-03 clock 1 hour backward
# Token now valid for 2 hours instead of 1
```

**Impact:** CRITICAL - Time manipulation bypasses all time-based security

**Mitigations:**

```yaml
Option 1: Multiple NTP sources + consensus (recommended)
  - Query 3-5 NTP servers: pool.ntp.org, time.google.com, time.cloudflare.com
  - Require majority agreement (median time)
  - Reject outliers (> 5 sec deviation)

Option 2: Trusted time source
  - Cornerstone acts as trusted time reference
  - New Stones sync from Cornerstone (authenticated)
  - Cornerstone syncs from multiple public NTP servers

Option 3: Certificate-based time attestation
  - Timestamp in join request signed by trusted time authority
  - Rough Time Protocol (RFC 8915)
  - Detect time manipulation attempts
```

**Recommendation:** P0 - Implement Option 1 (NTP consensus)

---

### 🟡 HIGH: MAC Address Spoofing

**Attack Vector:** Bypass rate limiting by spoofing MAC address

```bash
# Attacker changes MAC after each failed attempt
for i in {1..1000}; do
    sudo ip link set eth0 address $(openssl rand -hex 6 | sed 's/\(..\)/\1:/g; s/:$//')
    garden-rake place stone RANDOM_CODE
done

# Rate limiting ineffective - each attempt uses different MAC
```

**Impact:** HIGH - Rate limiting bypassed, brute force becomes feasible

**Mitigations:**

```yaml
Option 1: Device fingerprinting (recommended)
  - Track: MAC + IP + TLS fingerprint + hostname
  - Require all 4 to match for rate limit bypass
  - Significantly harder to spoof

Option 2: Proof-of-work challenge
  - After 3 failed attempts, require solving puzzle
  - Difficulty increases exponentially (2^n)
  - Makes brute force computationally expensive

Option 3: mTLS for join requests
  - Require temporary certificate for join attempt
  - Certificate issued after initial handshake
  - Rate limit by certificate serial number
```

**Recommendation:** P1 - Implement Option 1 (fingerprinting)

---

### 🟡 HIGH: Certificate Revocation Missing

**Attack Vector:** Compromised Stone cannot be evicted from pond

```bash
# stone-03 is compromised (attacker has private key)
# Admin tries to remove from pond
garden-rake revoke stone-03

# Problem: stone-03 still has valid certificate (365 days)
# Can still access pond services, API endpoints
# No Certificate Revocation List (CRL) mechanism
# Attacker continues to operate with valid certificate
```

**Impact:** HIGH - Compromised Stone has persistent access until cert expires

**Mitigations:**

```yaml
Option 1: Certificate Revocation List (CRL) (recommended)
  - Cornerstone maintains CRL (SQLite)
  - All Stones check CRL before accepting connections
  - Broadcast CRL updates via mDNS
  - OCSP Stapling for real-time validation

Option 2: Short-lived certificates + auto-renewal
  - Certificate validity: 1 hour (not 365 days)
  - Auto-renew every 30 min (background task)
  - Revocation = stop issuing renewals
  - Compromised Stone loses access in < 1 hour

Option 3: Pond-wide key rotation
  - garden-rake rotate pond-ca
  - Generate new CA key, re-issue all certificates
  - Old certificates immediately invalid
  - Nuclear option, requires all Stones online
```

**Recommendation:** P0 - Implement Option 2 (short-lived certs) + Option 1 (CRL) for P1

---

### 🟡 HIGH: Pebble Dictionary Attack

**Attack Vector:** Attacker with physical access brute forces pebble passphrase

```bash
# Attacker steals Stone, extracts pebble.enc
# Uses GPU to brute force Argon2id

# Argon2id parameters: 64MB memory, 3 iterations
# GPU attack: ~1000 H/s (AWS p3.16xlarge)
# Common passwords: 10M combinations
# Time to crack: 10M / 1000 / 3600 = 2.7 hours

# Once cracked:
# - Extract pond CA private key
# - Issue certificates for rogue Stones
# - Join pond without invitation
# - Impersonate any Stone
```

**Impact:** HIGH - Physical access + weak passphrase = full pond compromise

**Mitigations:**

```yaml
Option 1: Hardware security module (HSM) (recommended)
  - Store pebble in TPM 2.0 / Secure Enclave
  - Cannot extract, only decrypt with device
  - Physical theft useless without PIN/biometric

Option 2: Stronger KDF parameters
  - Increase Argon2id memory: 256MB (not 64MB)
  - Increase iterations: 10 (not 3)
  - Reduces GPU attack to ~50 H/s
  - Time to crack: 55 hours (still too short)

Option 3: Multi-factor pebble decryption
  - Require: passphrase + hardware token (YubiKey)
  - Or: passphrase + biometric (fingerprint)
  - Stealing Stone insufficient without 2nd factor

Option 4: Passphrase entropy requirements
  - Minimum: 20 characters (80-bit entropy)
  - Enforce: zxcvbn strength meter
  - Generate: diceware passphrases (6+ words)
```

**Recommendation:** P1 - Implement Option 1 (TPM) + Option 4 (entropy requirements)

---

### 🟡 HIGH: Used Codes Desynchronization

**Attack Vector:** Network partition causes inconsistent used codes state

```
Scenario:
1. User on stone-01 generates code: AJ4R9X
2. Attacker on new-stone types AJ4R9X
3. stone-02 wins election, handles join
4. stone-02 validates code, broadcasts "code used"
5. Network partition: stone-02 isolated from stone-03, stone-04
6. stone-03, stone-04 never receive "code used" broadcast
7. Partition heals after 10 minutes
8. Attacker captures previous encrypted join request
9. Replays to stone-03 (doesn't know code is used)
10. stone-03 accepts (code not in blocklist)
11. Attacker successfully joins with used code
```

**Impact:** HIGH - Code replay possible during network partitions

**Mitigations:**

```yaml
Option 1: Timestamp validation (recommended)
  - Reject codes older than 10 minutes (absolute)
  - Even if not in used_codes table
  - Limits replay window regardless of desync

Option 2: Quorum for used codes
  - Require 2/3 Stones confirm "code not used"
  - If partition prevents quorum, reject join
  - Availability hit, but prevents replay

Option 3: Vector clocks for used codes
  - Each broadcast includes vector clock
  - Detect inconsistencies on partition heal
  - Audit log for investigation

Option 4: Persistent used codes sync
  - On partition heal, sync used_codes tables
  - Use Merkle tree to efficiently detect differences
  - Automatic reconciliation
```

**Recommendation:** P0 - Implement Option 1 (timestamp validation) + Option 4 (sync) for P1

---

### 🟡 HIGH: Stone Impersonation After Join

**Attack Vector:** Compromised Stone impersonates another Stone

```bash
# Attacker compromises stone-03 (has certificate)
# Modifies Moss daemon to impersonate stone-01

# Modified moss.rs
fn my_stone_name() -> String {
    "stone-01".to_string()  // Lie about identity
}

# Attacker calls API on stone-02:
POST https://stone-02.local:3001/v1/services/offer
Headers:
  Authorization: Bearer <token_signed_by_stone-03_key>
  X-Stone-Name: stone-01

# Problem: Bearer token signature valid (stone-03 key)
# But claims to be stone-01
# stone-02 trusts X-Stone-Name header without verification
```

**Impact:** HIGH - Compromised Stone can frame others for malicious actions

**Mitigations:**

```yaml
Option 1: Certificate Common Name binding (recommended)
  - Certificate CN = stone-XX (immutable)
  - Extract CN from mTLS handshake
  - Ignore X-Stone-Name header
  - Trust only certificate identity

Option 2: Bearer token includes certificate hash
  - Token payload: {stone_name, cert_sha256, operation, timestamp}
  - Receiving Stone validates: cert_sha256 matches presented cert
  - Prevents impersonation with valid cert

Option 3: Audit all operations with certificate serial
  - Log: operation, claimed identity, cert serial, IP
  - Detective control: Identify impersonation attempts
  - Alert on mismatch: stone_name != cert CN
```

**Recommendation:** P0 - Implement Option 1 (CN binding)

---

### 🟡 MEDIUM: Denial of Service via Join Floods

**Attack Vector:** Attacker floods pond with join requests

```bash
# Attacker spawns 1000 VMs with spoofed MAC addresses
for i in {1..1000}; do
    (
        stone_name="rogue-$(uuidgen)"
        # Broadcast join request (triggers election on all Stones)
        mdns_announce "_koan-stone._tcp.local." "join_request" "$stone_name"
    ) &
done

# Result:
# - All pond Stones process 1000 elections simultaneously
# - CPU exhausted calculating hash delays
# - Legitimate joins blocked (rate limit exhausted)
# - mDNS network congestion
```

**Impact:** MEDIUM - Pond becomes unavailable for legitimate joins

**Mitigations:**

```yaml
Option 1: Proof-of-work for join requests (recommended)
  - Require solving hash puzzle before join accepted
  - Difficulty: Partial hash collision (e.g., 16-bit)
  - Computationally expensive for attacker (1000x slower)

Option 2: Join rate limiting (pond-wide)
  - Max 10 joins per hour (across all Stones)
  - Tracked in shared state (SQLite + broadcast)
  - Prevents exhausting pond capacity

Option 3: Invitation-only mode
  - Disable spontaneous joins
  - Only accept invited joins (with pre-generated codes)
  - Admin explicitly allows each Stone

Option 4: Network-level filtering
  - Firewall rules: Only allow join requests from local network
  - mDNS scope limited to .local (not routable)
  - Already mitigated if physical network secured
```

**Recommendation:** P1 - Implement Option 2 (pond-wide rate limit) + Option 4 (network scope)

---

### 🟡 MEDIUM: Split-Brain Scenario

**Attack Vector:** Network partition creates two separate ponds

```
Initial state: Pond with stones 01-04

Network partition:
  Partition A: stone-01, stone-02 (can communicate)
  Partition B: stone-03, stone-04 (can communicate)

Both partitions continue operating:

Partition A:
  - User adds stone-05 (successfully joins)
  - stone-01 issues certificate (CA key available)

Partition B:
  - User adds stone-06 (successfully joins)
  - stone-03 issues certificate (CA key available - replica)

Network heals:
  - stone-05 and stone-06 both in pond
  - Conflicting state: different certificates, different used codes
  - stone-05 cannot authenticate with stone-03 (unknown cert)
  - stone-06 cannot authenticate with stone-01 (unknown cert)
```

**Impact:** MEDIUM - Pond fragmentation requires manual reconciliation

**Mitigations:**

```yaml
Option 1: Quorum requirement (recommended)
  - Require 2/3 Stones online to accept joins
  - Minority partition cannot add Stones
  - Prevents split-brain

Option 2: Partition detection
  - Each Stone broadcasts heartbeat every 30 sec
  - If < 50% Stones visible, enter read-only mode
  - Block joins, config changes until majority visible

Option 3: Designated primary (Cornerstone only)
  - Only Cornerstone can issue certificates
  - Replicas cannot issue during partition
  - Single authoritative source

Option 4: Merkle tree reconciliation
  - On partition heal, compare state trees
  - Detect conflicting joins, revoke newer one
  - Automatic conflict resolution (last-write-wins)
```

**Recommendation:** P1 - Implement Option 2 (partition detection) + Option 4 (reconciliation)

---

### 🟡 MEDIUM: Audit Log Tampering

**Attack Vector:** Compromised Stone modifies audit logs

```bash
# Attacker compromises stone-03
# Covers tracks by deleting audit entries

sudo sqlite3 /var/lib/zen-garden/audit.db
DELETE FROM audit_log WHERE stone_name = 'rogue-stone-01';
DELETE FROM audit_log WHERE event = 'unauthorized_access';

# No cryptographic integrity protection
# Attacker removes evidence of compromise
# Incident response impossible (logs unreliable)
```

**Impact:** MEDIUM - Cannot detect or investigate security incidents

**Mitigations:**

```yaml
Option 1: Append-only log with signatures (recommended)
  - Each entry signed with Stone private key
  - Chain signatures (entry N signs hash of entry N-1)
  - Blockchain-style, tamper-evident
  - Deletion breaks signature chain

Option 2: Centralized log aggregation
  - Forward logs to external syslog server
  - Compromised Stone cannot modify remote logs
  - Require mTLS for log transmission

Option 3: Distributed log replication
  - Broadcast audit events to all Stones
  - Each Stone stores full audit history
  - Tampering requires compromising all Stones

Option 4: Write-once storage
  - Use append-only filesystem (SquashFS, WORM)
  - Or: S3 bucket with object lock
  - Physically impossible to modify logs
```

**Recommendation:** P1 - Implement Option 1 (signed logs) + Option 3 (replication)

---

### 🟢 LOW: Bearer Token Nonce Replay (Short TTL)

**Attack Vector:** Restart wipes nonce cache, enables replay

```bash
# Attacker captures bearer token (TTL: 5 min, long mode)
# stone-02 validates, adds nonce to in-memory cache
# Attacker waits...
# stone-02 restarts (power outage, crash, update)
# In-memory nonce cache wiped
# Attacker replays token within 5 min window
# stone-02 accepts (nonce not in cache)
```

**Impact:** LOW - Replay window limited to 5 min + rare restart

**Mitigations:**

```yaml
Option 1: Persistent nonce storage (recommended)
  - Store nonces in SQLite (already planned)
  - Survives restarts
  - Cleanup: Delete nonces older than max TTL (1 hour)

Option 2: Nonce expiration tied to token TTL
  - Token payload includes: {nonce, expires_at}
  - Store nonces with expiration timestamp
  - Auto-cleanup on query (DELETE WHERE expires_at < now())

Option 3: Invalidate all tokens on restart
  - Broadcast "stone-02 restarted" to pond
  - All Stones clear cached tokens from stone-02
  - Requires re-authentication after restart
```

**Recommendation:** P0 - Already planned (SQLite persistence), implement Option 2 (TTL-based expiry)

---

### 🟢 LOW: mDNS Announcement Flooding

**Attack Vector:** Attacker floods mDNS with fake service announcements

```bash
# Attacker on local network
while true; do
    avahi-publish -s "mongodb-$(uuidgen)" _moss._tcp 8080 "protocol=native"
    avahi-publish -s "redis-$(uuidgen)" _moss._tcp 8081 "protocol=native"
done

# Result:
# - garden-rake list services shows 1000s of fake entries
# - User confused, cannot find real services
# - Annoyance, not security breach (no data access)
```

**Impact:** LOW - Cosmetic issue, legitimate services still accessible

**Mitigations:**

```yaml
Option 1: Filter by TLS fingerprint (recommended)
  - Only show services from Stones with valid certificates
  - Verify mTLS before listing service
  - Attacker cannot fake (no private key)

Option 2: Service announcement signatures
  - Sign mDNS TXT records with Stone private key
  - garden-rake verifies signature before displaying
  - Unsigned announcements ignored

Option 3: Allowlist known Stones
  - Configuration: allowed_stones = [stone-01, stone-02, ...]
  - Ignore announcements from unknown sources
  - Requires manual maintenance
```

**Recommendation:** P2 - Implement Option 1 (certificate filtering)

---

## Security Scoring Matrix

| Vulnerability              | Severity | Exploitability | Impact                         | Priority |
| -------------------------- | -------- | -------------- | ------------------------------ | -------- |
| Config propagation exploit | CRITICAL | High           | Complete pond compromise       | P0       |
| Predictable election       | CRITICAL | Medium         | Targeted DoS                   | P0       |
| Time oracle attack         | CRITICAL | High           | Bypass all time-based security | P0       |
| MAC spoofing               | HIGH     | High           | Brute force codes              | P1       |
| Missing cert revocation    | HIGH     | Low            | Persistent compromised access  | P0       |
| Pebble dictionary attack   | HIGH     | Medium         | Full pond compromise           | P1       |
| Used codes desync          | HIGH     | Low            | Code replay during partition   | P0       |
| Stone impersonation        | HIGH     | Medium         | Frame other Stones             | P0       |
| Join flood DoS             | MEDIUM   | High           | Pond unavailable               | P1       |
| Split-brain                | MEDIUM   | Low            | State inconsistency            | P1       |
| Audit log tampering        | MEDIUM   | Medium         | Hide compromise                | P1       |
| Bearer token replay        | LOW      | Low            | Limited replay window          | P0       |
| mDNS flooding              | LOW      | High           | Cosmetic issue                 | P2       |

---

## Mandatory Fixes (P0 - Block Production)

### 1. Configuration ACL

```rust
// Only Cornerstone can change security-critical configs
const SECURITY_CONFIGS: &[&str] = &[
    "bearer_token_ttl",
    "rate_limit_attempts",
    "rate_limit_ban_duration",
    "code_ttl_minutes",
];

fn update_config(key: &str, value: &str) -> Result<()> {
    if SECURITY_CONFIGS.contains(&key) && !am_i_cornerstone()? {
        return Err(Error::Unauthorized(
            "Only Cornerstone can modify security configurations"
        ));
    }

    // Proceed with update...
}
```

### 2. Non-Deterministic Election

```rust
// Add random salt to election delay calculation
fn calculate_election_delay(new_stone: &str, salt: &[u8]) -> u64 {
    let payload = format!("{}:{}:{}", my_stone_name(), new_stone, hex::encode(salt));
    let hash = sha256(payload.as_bytes());
    u64::from_be_bytes([hash[0], hash[1], 0, 0, 0, 0, 0, 0]) % 5000
}

// New Stone includes random salt in join request
let salt = rand::random::<[u8; 16]>();
let join_req = JoinRequest {
    stone_name: hostname(),
    pubkey: my_pubkey(),
    timestamp: now(),
    election_salt: salt,  // ← Added
};
```

### 3. NTP Consensus

```rust
async fn sync_time_with_consensus() -> Result<SystemTime> {
    let servers = [
        "pool.ntp.org",
        "time.google.com",
        "time.cloudflare.com",
    ];

    let mut times = Vec::new();
    for server in servers {
        if let Ok(time) = query_ntp(server).await {
            times.push(time);
        }
    }

    if times.len() < 2 {
        return Err(Error::NtpSyncFailed("Not enough NTP sources"));
    }

    times.sort();
    let median = times[times.len() / 2];

    // Reject if any source deviates > 5 sec from median
    for time in &times {
        if time.duration_since(median)?.as_secs() > 5 {
            return Err(Error::NtpOutlierDetected);
        }
    }

    Ok(median)
}
```

### 4. Absolute Timestamp Validation

```rust
fn validate_code(code: &str, timestamp: u64) -> Result<()> {
    let now = current_time();
    let code_age = now - timestamp;

    // Reject codes older than 10 minutes (absolute)
    if code_age > 600 {
        return Err(Error::CodeExpired(code_age));
    }

    // Check time windows...
}
```

### 5. Short-Lived Certificates

```rust
fn issue_certificate(stone_name: &str, pubkey: &PublicKey) -> Certificate {
    Certificate::new()
        .common_name(stone_name)
        .validity_duration(Duration::hours(1))  // Not 365 days
        .sign_with(pond_ca_key())
}

// Auto-renewal background task
async fn auto_renew_certificate() {
    loop {
        sleep(Duration::minutes(30)).await;

        if cert_expires_in() < Duration::minutes(45) {
            request_certificate_renewal().await?;
        }
    }
}
```

### 6. Certificate CN Binding

```rust
fn verify_bearer_token(token: &str, tls_conn: &TlsStream) -> Result<String> {
    let claims: TokenClaims = decode_token(token)?;

    // Extract Common Name from mTLS certificate
    let peer_cert = tls_conn.peer_certificate()?;
    let cert_cn = peer_cert.subject_common_name()?;

    // Ignore claimed identity, trust only certificate
    if claims.stone_name != cert_cn {
        return Err(Error::IdentityMismatch {
            claimed: claims.stone_name,
            cert_cn,
        });
    }

    Ok(cert_cn)
}
```

---

## Developer-Friendly Security Alternatives

**Philosophy:** Security shouldn't require a PhD. Every P0 fix includes a user-facing component that makes security visible, understandable, and recoverable.

---

### Alternative 1: Configuration Safety Net (vs Strict ACL)

**Problem with Strict ACL:** Blocks legitimate changes, frustrating for solo admin scenarios

**DX-Friendly Approach:**

```rust
// Progressive security: Warn → Confirm → Audit
fn update_config(key: &str, value: &str) -> Result<()> {
    if SECURITY_CONFIGS.contains(&key) {
        // Visual warning (always shown)
        eprintln!("⚠️  Changing security configuration: {}", key);
        eprintln!("   Current value: {}", config::get(key)?);
        eprintln!("   New value: {}", value);
        eprintln!("   Impact: All {} Stones in pond", pond_stone_count()?);
        eprintln!();

        // Confirmation prompt (unless --yes flag)
        if !confirm("Apply this change pond-wide?")? {
            return Ok(()); // User cancelled
        }

        // Audit trail (always logged)
        audit_log(AuditEvent::ConfigChanged {
            key,
            old_value: config::get(key)?,
            new_value: value,
            stone: my_stone_name(),
            user: env::var("USER").unwrap_or("unknown".into()),
            timestamp: now(),
        })?;
    }

    // Apply change + broadcast
    config::set(key, value)?;
    broadcast_config_update(key, value)?;

    Ok(())
}
```

**User Experience:**

```bash
$ garden-rake config set bearer_token_ttl long

⚠️  Changing security configuration: bearer_token_ttl
   Current value: short (5 minutes)
   New value: long (1 hour)
   Impact: All 4 Stones in pond

   ⚠️  Security implications:
      • Longer replay window (5 min → 1 hour)
      • Requires nonce tracking (automatic)
      • Recommended only for slow networks

Apply this change pond-wide? [y/N] y

✓ Configuration updated on all Stones
✓ Nonce tracking enabled (replay protection active)

Recent changes (garden-rake config history):
  2026-01-15 12:34  bearer_token_ttl: short → long (you, from stone-02)
  2026-01-14 10:15  rate_limit_attempts: 5 → 10 (admin, from stone-01)

Undo this change: garden-rake config rollback
```

**Benefits:**

- ✅ User understands impact before changing
- ✅ Easy rollback if mistake made
- ✅ Audit trail for troubleshooting
- ✅ Works for solo admin (no multi-party approval needed)

**Additional Safety:**

```bash
# Rate limiting on config changes (prevent rapid escalation)
garden-rake config set rate_limit_attempts 100
⚠️  You've made 5 configuration changes in the last hour
   Rate limit: 10 changes/hour (recommended for security)
   Remaining: 5 changes

Proceed anyway? [y/N]

# Automatic revert on Stone failures
# If >50% Stones reject config, auto-rollback
✗ Configuration rejected by 3/4 Stones
  Reason: Invalid value for bearer_token_ttl
✓ Rolled back to previous value: short

# Visual config health in status
$ garden-rake status

Pond: My Garden (4 Stones)
Configuration:
  ✓ bearer_token_ttl: short (default, recommended)
  ⚠️ rate_limit_attempts: 20 (custom, default: 5)
     ^ Higher than recommended, consider reverting

Recent changes: 2 in last 24h (view: garden-rake config history)
```

---

### Alternative 2: User-Directed Election (vs Pure Random)

**Problem with Pure Random:** User has no control, Stone may be slow/busy

**DX-Friendly Approach:**

```rust
// Spontaneous join with user choice
async fn handle_spontaneous_join(new_stone: &str) -> Result<()> {
    // Calculate election winners (top 3 candidates)
    let candidates = calculate_election_candidates(new_stone, 3)?;

    // Notify user with options
    let notification = json!({
        "event": "stone_join_request",
        "stone_name": new_stone,
        "candidates": candidates, // [stone-02, stone-01, stone-04]
        "auto_select_in": 10, // seconds
    });

    broadcast_notification(notification)?;

    // Wait for user selection (10 sec timeout)
    match wait_for_user_selection(Duration::from_secs(10)).await {
        Ok(selected) => selected,
        Err(_) => candidates[0].clone(), // Auto-select first if timeout
    }
}
```

**User Experience:**

```bash
# Desktop notification (interactive)
┌─────────────────────────────────────────────────┐
│ 🔔 New Stone wants to join: old-laptop-01       │
│                                                 │
│ Which Stone should handle this?                 │
│   • stone-02 (recommended, least loaded)        │
│   • stone-01 (current device)                   │
│   • stone-04                                    │
│                                                 │
│ Auto-selecting stone-02 in 10 seconds...        │
│                                                 │
│ [Select stone-01] [Let system decide] [Reject]  │
└─────────────────────────────────────────────────┘

# If user clicks "Select stone-01"
✓ You will handle join request from old-laptop-01
  Generating invitation code... ✓

  Invitation Code: AJ4R9X (valid 5 min)

  Walk to old-laptop-01 and type this code.

# CLI version (non-interactive environment)
$ garden-rake status

Pending join requests:
  • old-laptop-01 (3 seconds ago)
    Suggested handler: stone-02 (least loaded)
    Auto-accepting in 7 seconds...

    Override: garden-rake handle-join old-laptop-01 --on stone-01
    Reject: garden-rake reject-join old-laptop-01
```

**Benefits:**

- ✅ User has control (can pick specific Stone)
- ✅ System provides smart default (least loaded)
- ✅ Works automatically if user unavailable (10 sec timeout)
- ✅ Visual feedback on which Stone is handling

---

### Alternative 3: Relative Time Validation (vs NTP Consensus)

**Problem with NTP Consensus:** Requires internet, complex setup, failure modes

**DX-Friendly Approach:**

```rust
// Trust Cornerstone as time source, validate relatively
async fn validate_join_request(req: &JoinRequest) -> Result<()> {
    // 1. Check absolute time difference (coarse filter)
    let my_time = SystemTime::now();
    let req_time = UNIX_EPOCH + Duration::from_secs(req.timestamp);
    let time_diff = my_time.duration_since(req_time)
        .unwrap_or_else(|_| req_time.duration_since(my_time).unwrap());

    // Reject if wildly out of sync (>1 hour = probable attack)
    if time_diff > Duration::from_secs(3600) {
        return Err(Error::TimeDriftExcessive(time_diff.as_secs()));
    }

    // 2. Validate code with flexible window (±10 min instead of ±5)
    //    Compensates for clock drift without requiring NTP
    for offset in -2..=2 {  // 5 windows instead of 3
        let window = (req.timestamp / 300) + offset;
        if validate_code_for_window(&req.code, window)? {
            return Ok(());
        }
    }

    // 3. If still invalid, suggest time sync to user
    Err(Error::CodeInvalid {
        reason: "Clock drift detected",
        suggestion: "Try: garden-rake sync-time",
    })
}

// Simple time sync (tries NTP, falls back to Cornerstone)
async fn sync_time_simple() -> Result<()> {
    // Try NTP (best effort, single server)
    if let Ok(time) = query_ntp("pool.ntp.org").await.timeout(5.secs()) {
        set_system_time(time)?;
        return Ok(());
    }

    // Fallback: Ask Cornerstone for time
    let cornerstone_time = http_get("https://cornerstone.local:3001/time").await?;

    eprintln!("⚠️  NTP unavailable, using Cornerstone time");
    eprintln!("   Cornerstone: {}", cornerstone_time);
    eprintln!("   Your time: {}", SystemTime::now());
    eprintln!();

    if confirm("Use Cornerstone time?")? {
        set_system_time(cornerstone_time)?;
        Ok(())
    } else {
        Err(Error::TimeSyncDeclined)
    }
}
```

**User Experience:**

```bash
# Automatic handling (most cases)
$ garden-rake place stone AJ4R9X

Checking time synchronization...
  ⚠️ Clock drift detected: 7 minutes behind
  Syncing with Cornerstone... ✓ (no NTP required)

Joining pond... ✓

# Manual sync when needed
$ garden-rake sync-time

Attempting time synchronization...
  [1/3] Trying pool.ntp.org... ✓ (2026-01-15 12:35:00 UTC)

✓ Time synchronized via NTP
  Drift corrected: +7 minutes

# NTP unavailable (offline network)
$ garden-rake sync-time

Attempting time synchronization...
  [1/3] Trying pool.ntp.org... ✗ (timeout)
  [2/3] Trying time.google.com... ✗ (no internet)
  [3/3] Asking Cornerstone... ✓ (2026-01-15 12:35:00 UTC)

⚠️  NTP unavailable, using Cornerstone time
   Cornerstone: 2026-01-15 12:35:00 UTC
   Your time:  2026-01-15 12:28:00 UTC (7 min behind)

Use Cornerstone time? [Y/n] y

✓ Time synchronized with Cornerstone

ⓘ This Stone now trusts Cornerstone as time source
  Ensure Cornerstone time is accurate
  Check: garden-rake status --time
```

**Benefits:**

- ✅ Works offline (Cornerstone fallback)
- ✅ Wider time window (±10 min) reduces false rejections
- ✅ User-friendly sync command (one step)
- ✅ Transparent about time source (NTP vs Cornerstone)

**Additional Safety:**

```bash
# Visual time health in status
$ garden-rake status --time

Time Synchronization Status:

This Stone (stone-02):
  System time: 2026-01-15 12:35:00 UTC
  Last NTP sync: 2 hours ago (pool.ntp.org)
  Drift: < 1 second (excellent)

Cornerstone (stone-01):
  System time: 2026-01-15 12:35:01 UTC
  Last NTP sync: 10 minutes ago (pool.ntp.org)
  Authority: Primary time source for pond

Other Stones:
  stone-03: ✓ Synced (drift: 2 sec)
  stone-04: ⚠️ Out of sync (drift: 8 min) - run: garden-rake sync-time --on stone-04

⚠️  1 Stone has excessive drift (> 5 min)
   Join requests may fail from this Stone
   Recommendation: Sync time on all Stones

Fix all: garden-rake sync-time --all
```

---

### Alternative 4: Transparent Partition Handling (vs Quorum)

**Problem with Quorum:** Complex, requires majority online, availability hit

**DX-Friendly Approach:**

```rust
// Detect partition, inform user, graceful degradation
async fn check_pond_health() -> PondHealth {
    let all_stones = load_pond_manifest()?;
    let visible_stones = discover_stones_via_mdns(Duration::from_secs(5)).await?;

    let health = PondHealth {
        total: all_stones.len(),
        visible: visible_stones.len(),
        partition_detected: visible_stones.len() < all_stones.len() / 2,
    };

    // User notification on partition
    if health.partition_detected {
        eprintln!("⚠️  NETWORK PARTITION DETECTED");
        eprintln!("   Visible Stones: {}/{}", health.visible, health.total);
        eprintln!("   Missing: {}", missing_stones(&all_stones, &visible_stones).join(", "));
        eprintln!();
        eprintln!("   Pond is now in READ-ONLY mode");
        eprintln!("   • Can list services: garden-rake list");
        eprintln!("   • Cannot add Stones: garden-rake invite (blocked)");
        eprintln!("   • Cannot change configs: garden-rake config (blocked)");
        eprintln!();
        eprintln!("   This protects against split-brain scenarios.");
        eprintln!("   Normal operations resume when partition heals.");
        eprintln!();
    }

    health
}

// Graceful degradation: Block risky operations, allow safe ones
fn require_pond_quorum(operation: &str) -> Result<()> {
    let health = check_pond_health().await?;

    if health.partition_detected {
        return Err(Error::PartitionDetected {
            operation,
            visible: health.visible,
            required: health.total / 2 + 1,
            help: "Wait for network to heal, or use --force (not recommended)",
        });
    }

    Ok(())
}
```

**User Experience:**

```bash
# Partition detected automatically
$ garden-rake invite stone

⚠️  NETWORK PARTITION DETECTED
   Visible Stones: 2/4
   Missing: stone-03, stone-04

   Pond is now in READ-ONLY mode
   • Can list services: garden-rake list ✓
   • Cannot add Stones: garden-rake invite ✗
   • Cannot change configs: garden-rake config ✗

   This protects against split-brain scenarios.
   Normal operations resume when partition heals.

✗ Cannot invite Stones during partition
  Reason: Risk of conflicting certificates
  Visible: 2/4 Stones (need 3/4 for quorum)

  Wait for network to heal, then retry.

  Advanced: garden-rake invite stone --force
  (Use only if you're certain this is the primary partition)

# Partition heals (automatic detection)
✓ Network partition healed
  All 4 Stones now visible
  Reconciling state... ✓

  Pond returned to NORMAL mode
  All operations now available

# Status command shows partition health
$ garden-rake status

Pond: My Garden (4 Stones)
Status: ⚠️ DEGRADED - Network partition detected

Visible Stones (2/4):
  ✓ stone-01 (cornerstone)
  ✓ stone-02 (current)

Missing Stones (2/4):
  ⚠️ stone-03 (last seen 5 minutes ago)
  ⚠️ stone-04 (last seen 5 minutes ago)

Operations:
  ✓ List services (read-only)
  ✗ Invite Stones (blocked until partition heals)
  ✗ Change configs (blocked until partition heals)

Troubleshooting:
  1. Check network connectivity: ping stone-03.local
  2. Check Stone status: ssh stone-03 'systemctl status garden-moss.service'
  3. View partition history: garden-rake events --filter partition

ⓘ This is normal during network maintenance
  Pond will auto-recover when all Stones reconnect
```

**Benefits:**

- ✅ Partition detected automatically (no config needed)
- ✅ Clear explanation of degraded state
- ✅ Blocks dangerous operations, allows safe ones
- ✅ Auto-recovery when partition heals
- ✅ Override available for advanced users (--force)

---

### Alternative 5: Visual Certificate Lifecycle (vs Complex CRL)

**Problem with CRL:** Infrastructure overhead, complex broadcast, failure modes

**DX-Friendly Approach:**

```rust
// Short-lived certs (1 hour) + visible renewal + simple revocation
async fn certificate_lifecycle_monitor() {
    loop {
        let cert = load_my_certificate()?;
        let expires_in = cert.not_after() - SystemTime::now();

        // Visual status in logs
        if expires_in < Duration::from_minutes(15) {
            eprintln!("ⓘ Certificate expires in {} minutes", expires_in.as_secs() / 60);
            eprintln!("  Auto-renewal will trigger at 15 min remaining");
        }

        // Auto-renew at 30 min remaining (50% lifetime)
        if expires_in < Duration::from_minutes(30) {
            match renew_certificate().await {
                Ok(_) => {
                    eprintln!("✓ Certificate renewed successfully");
                    eprintln!("  New expiration: {}", cert.not_after());
                    audit_log("certificate_renewed")?;
                }
                Err(e) => {
                    eprintln!("⚠️  Certificate renewal failed: {}", e);
                    eprintln!("   Retrying in 5 minutes...");
                    eprintln!("   If this persists, run: garden-rake cert renew --manual");
                }
            }
        }

        sleep(Duration::from_minutes(5)).await;
    }
}

// Simple revocation: Stop issuing renewals
async fn revoke_stone(stone_name: &str, reason: &str) -> Result<()> {
    // Add to revocation list (simple SQLite)
    db::execute(
        "INSERT INTO revoked_stones (name, reason, revoked_at, revoked_by) VALUES (?, ?, ?, ?)",
        (stone_name, reason, now(), my_stone_name())
    )?;

    // Broadcast to pond
    broadcast_event(PondEvent::StoneRevoked {
        name: stone_name.to_string(),
        reason: reason.to_string(),
        expires_at: now() + Duration::from_hours(1), // Max 1 hour until cert expires
    })?;

    // User-friendly output
    println!("✓ Stone revoked: {}", stone_name);
    println!("  Reason: {}", reason);
    println!("  Certificate expires in: < 1 hour (automatic)");
    println!("  Access will be blocked pond-wide immediately");
    println!();
    println!("  The revoked Stone will lose access when:");
    println!("  1. All Stones receive revocation broadcast (< 30 sec)");
    println!("  2. Certificate expires (< 1 hour)");
    println!();
    println!("View revoked Stones: garden-rake list stones --revoked");

    Ok(())
}
```

**User Experience:**

```bash
# Revoke compromised Stone (simple command)
$ garden-rake revoke stone-03 --reason "Suspected compromise"

✓ Stone revoked: stone-03
  Reason: Suspected compromise
  Certificate expires in: 45 minutes (automatic)
  Access will be blocked pond-wide immediately

Revocation broadcast sent to:
  ✓ stone-01 (acknowledged)
  ✓ stone-02 (acknowledged)
  ✓ stone-04 (acknowledged)

stone-03 can no longer:
  • Renew certificate (blocked by Cornerstone)
  • Access pond services (mTLS rejected)
  • Run commands (bearer tokens invalid)

Certificate will expire automatically in 45 minutes.

Next steps:
  1. Investigate compromise: ssh stone-03 'garden-rake audit'
  2. Rebuild Stone if needed: garden-rake reset stone-03
  3. Re-join if false alarm: garden-rake unrevo stone-03

# View certificate status (visual)
$ garden-rake status --certs

Certificate Status:

stone-01 (cornerstone):
  ✓ Valid (expires in 42 minutes)
  Auto-renewal: Scheduled in 12 minutes
  Serial: 6a7f9e3c...

stone-02 (current):
  ✓ Valid (expires in 38 minutes)
  Auto-renewal: Scheduled in 8 minutes
  Serial: 4b2d8a1f...

stone-03:
  ✗ REVOKED (Suspected compromise)
  Revoked: 2026-01-15 12:30:00 by stone-01
  Expires: 2026-01-15 13:15:00 (23 minutes)

stone-04:
  ⚠️  Expires soon (7 minutes)
  Auto-renewal: In progress...

⚠️  1 Stone revoked, 1 expiring soon
   All others healthy

# Auto-renewal visible in logs
$ journalctl -fu moss

Jan 15 12:30:00 moss[1234]: ⓘ Certificate expires in 30 minutes
Jan 15 12:30:00 moss[1234]:   Requesting renewal from Cornerstone...
Jan 15 12:30:01 moss[1234]: ✓ Certificate renewed successfully
Jan 15 12:30:01 moss[1234]:   Old expiration: 2026-01-15 13:00:00
Jan 15 12:30:01 moss[1234]:   New expiration: 2026-01-15 14:00:00
Jan 15 12:30:01 moss[1234]:   Serial: 6a7f9e3c... → 8d4c2b5a...
```

**Benefits:**

- ✅ Revocation takes effect immediately (< 30 sec broadcast)
- ✅ No complex CRL infrastructure needed
- ✅ Auto-renewal visible in logs (transparency)
- ✅ Manual renewal available if auto fails
- ✅ Visual certificate health in status command

---

### Alternative 6: Identity Verification with User Context

**Problem with Silent CN Binding:** User unaware of identity checks, no feedback

**DX-Friendly Approach:**

```rust
// Certificate CN binding + visual feedback + audit trail
fn verify_request_identity(req: &Request, tls_conn: &TlsStream) -> Result<StoneIdentity> {
    // Extract identity from certificate (trusted)
    let cert = tls_conn.peer_certificate()?;
    let cert_cn = cert.subject_common_name()?;
    let cert_serial = cert.serial_number()?;

    // Extract claimed identity from request
    let claimed_identity = req.headers().get("X-Stone-Name")
        .and_then(|h| h.to_str().ok())
        .unwrap_or("");

    // Verify match
    if claimed_identity != cert_cn {
        // Log mismatch (suspicious activity)
        audit_log(AuditEvent::IdentityMismatch {
            claimed: claimed_identity.to_string(),
            cert_cn: cert_cn.clone(),
            cert_serial: cert_serial.clone(),
            endpoint: req.uri().path().to_string(),
            source_ip: req.peer_addr(),
        })?;

        // Alert user (visual notification)
        alert_user(Alert {
            level: AlertLevel::Warning,
            title: "Suspicious Activity Detected",
            message: format!(
                "Stone {} claimed to be {} (mismatch)\nConnection blocked for safety",
                cert_cn, claimed_identity
            ),
            actions: vec![
                ("View Details", "garden-rake audit | grep identity_mismatch"),
                ("Investigate", format!("garden-rake logs {}", cert_cn)),
            ],
        })?;

        return Err(Error::IdentityMismatch {
            claimed: claimed_identity.to_string(),
            cert_cn,
            help: "This may indicate a compromised Stone attempting impersonation",
        });
    }

    // Valid identity
    Ok(StoneIdentity {
        name: cert_cn,
        serial: cert_serial,
        verified: true,
    })
}
```

**User Experience:**

```bash
# Identity verification works silently (normal case)
$ garden-rake offer mongodb --at stone-03

Connecting to stone-03...
  ✓ Certificate verified (stone-03)
  ✓ Identity confirmed

Offering mongodb on stone-03... ✓

# Identity mismatch detected (visual alert)
$ garden-rake offer mongodb --at stone-03

Connecting to stone-03...
  ✗ Identity verification failed

⚠️  SUSPICIOUS ACTIVITY DETECTED

Stone presented certificate for: stone-04
But claimed to be: stone-03

This may indicate:
  • Compromised Stone attempting impersonation
  • Certificate misconfiguration
  • Network man-in-the-middle attack

Connection blocked for safety.

Actions:
  1. Verify stone-03 certificate: garden-rake status stone-03 --cert
  2. Check audit log: garden-rake audit | grep stone-03
  3. Investigate stone-04: garden-rake investigate stone-04

This incident has been logged for review.

# Audit trail (searchable)
$ garden-rake audit --filter identity_mismatch

2026-01-15 12:30:00 | WARN | Identity Mismatch
  Claimed: stone-03
  Certificate: stone-04
  Serial: 8d4c2b5a...
  Endpoint: /v1/services/offer
  Source IP: 192.168.1.14
  Action: Connection blocked

2026-01-15 12:31:00 | INFO | Investigation Started
  Target: stone-04
  Initiated by: you (stone-02)

# Visual identity in all operations
$ garden-rake list stones

Stones in Pond (4):
  stone-01 (cornerstone)
    ✓ Identity verified
    Certificate: 6a7f9e3c... (expires in 42 min)
    Last seen: 2 seconds ago

  stone-02 (current)
    ✓ Identity verified (you)
    Certificate: 4b2d8a1f... (expires in 38 min)

  stone-03
    ⚠️ Identity verification failed (1 incident)
    Certificate: ??? (mismatch detected)
    Last seen: 5 minutes ago
    Action needed: garden-rake investigate stone-03

  stone-04
    ✓ Identity verified
    Certificate: 8d4c2b5a... (expires in 35 min)
    Last seen: 10 seconds ago
```

**Benefits:**

- ✅ Identity verification automatic (no user action needed)
- ✅ Mismatch triggers immediate alert (visible)
- ✅ Clear explanation of security issue
- ✅ Actionable steps for investigation
- ✅ Audit trail for forensics

---

## DX Principle Summary

Every security fix includes three components:

1. **Prevention** (Technical): Cryptographic/protocol-level security
2. **Detection** (Monitoring): Visual feedback when issues occur
3. **Recovery** (User-Facing): Clear steps to fix problems

**Examples:**

| Vulnerability        | Prevention                         | Detection                        | Recovery                |
| -------------------- | ---------------------------------- | -------------------------------- | ----------------------- |
| Config exploit       | Confirmation prompt + audit log    | Recent changes visible in status | Rollback command (undo) |
| Predictable election | Random salt + user selection       | Visual election candidates       | Manual Stone selection  |
| Time oracle          | Cornerstone fallback + wide window | Time health in status            | Simple sync command     |
| Partition            | Auto read-only mode                | Partition detected message       | Auto-recovery on heal   |
| No revocation        | Short-lived certs + broadcast      | Certificate status visible       | Simple revoke command   |
| Impersonation        | CN binding + audit                 | Visual identity alerts           | Investigation workflow  |

**Design Philosophy:**

```yaml
Before (Enterprise Security):
  Configuration ACL:
    Approach: Strict RBAC, admin-only configs
    Problem: Requires IAM system, complex for solo admin

  NTP Consensus:
    Approach: Query 3 NTP servers, reject outliers
    Problem: Requires internet, complex failure handling

  Certificate Revocation:
    Approach: OCSP responder, CRL distribution
    Problem: Infrastructure overhead, single point of failure

After (Home Lab Security):
  Configuration Safety:
    Approach: Warn → Confirm → Audit → Rollback
    Benefit: User understands impact, easy recovery

  Time Sync:
    Approach: Try NTP, fallback Cornerstone, wide window
    Benefit: Works offline, simple command, auto-recovers

  Certificate Lifecycle:
    Approach: Short-lived (1h) + auto-renew + simple revoke
    Benefit: No infrastructure, auto-expires, visual status
```

**Core Tenant:** Security that requires reading documentation is security that won't be used. Make it visible, understandable, and recoverable.

---

## Security Tiers: Pragmatic Risk Assessment

**Critical Insight:** Home labs and enterprises face fundamentally different threat models. Designing one-size-fits-all security creates unnecessary complexity.

### Threat Model Comparison

| Threat                | Home Lab Reality                        | Enterprise Reality                 |
| --------------------- | --------------------------------------- | ---------------------------------- |
| Compromised Stone     | Physical access required, trusted users | Remote compromise, insider threat  |
| Network attacks       | Single admin controls network           | Multi-tenant, hostile network      |
| Configuration exploit | Admin shoots own foot                   | Lateral movement vector            |
| Time manipulation     | Unlikely (who controls home router?)    | APT-level attack possible          |
| Physical theft        | Home burglary (rare)                    | Data center breach, stolen laptops |
| Compliance            | None                                    | GDPR, SOC2, HIPAA                  |

### Proposed Security Tiers

---

## Tier 1: Garden Pond (Default - Home Lab)

**Target:** Solo admin, home lab, trusted local network, 2-10 Stones

**Philosophy:** Protect against accidents and casual mistakes, not nation-states

### Realistic Threats for Home Labs:

1. **User mistakes** (HIGH) - Admin misconfigures, breaks pond
2. **Network sniffing** (MEDIUM) - Family member on WiFi captures traffic
3. **Physical theft** (LOW) - Burglar steals Stone with pebble
4. **Malware on Stone** (LOW) - Compromised Stone in pond

### Tier 1 Security Posture:

**✅ Implemented (P0):**

- Configuration safety net (warn → confirm → rollback)
- Encrypted join requests (prevent WiFi sniffing)
- Short-lived certificates (1 hour, auto-renew)
- Certificate CN binding (prevent impersonation)
- Absolute timestamp validation (10 min window, no NTP consensus needed)
- Used codes tracking (SQLite)
- Visual security feedback (status command shows health)

**🔒 Simplified (vs Enterprise):**

- NTP sync: Single source (pool.ntp.org), Cornerstone fallback, no consensus
- Election: User-directed with smart defaults, not cryptographic challenge-response
- Partition handling: Visual warning + read-only mode, no quorum voting
- Pebble security: Strong passphrase required, no TPM/HSM mandatory
- Rate limiting: Simple MAC-based, manual unban via CLI
- Audit logs: SQLite local storage, no distributed replication

**⚠️ Accepted Risks:**

- Single admin can change security configs (trust model: home = trusted)
- Pebble extractable with physical access + weak passphrase (mitigation: passphrase entropy check)
- Network partition may allow split-brain (mitigation: manual reconciliation)
- Time oracle possible if attacker controls home router (mitigation: visual time drift warnings)

### Tier 1 Implementation Burden:

```yaml
Complexity: Low
- Configuration: None (sane defaults)
- Maintenance: Zero (auto-healing)
- User education: Minimal (visual feedback explains issues)

Development Effort:
- P0 fixes: 3-4 days (simplified alternatives)
- Testing: 2 days
- Documentation: 1 day
- Total: 1 week for production-ready home lab security
```

---

## Tier 2: Fortress Pond (Opt-In - Enterprise)

**Target:** Multi-admin, enterprise, untrusted network, 10+ Stones, compliance

**Philosophy:** Defense in depth, assume breach, regulatory compliance

### Additional Threats for Enterprise:

1. **Insider threat** (HIGH) - Malicious admin with valid credentials
2. **APT attacks** (HIGH) - Nation-state attackers, persistent threats
3. **Compliance** (HIGH) - Audit logs, encryption at rest, key rotation
4. **Lateral movement** (MEDIUM) - Compromised Stone pivots to others
5. **Supply chain** (MEDIUM) - Backdoored Stone joins pond

### Tier 2 Hardening (Additional to Tier 1):

**🔐 Configuration ACL:**

- Multi-signature for security config changes (2-of-3 admins)
- Quorum voting (66% approval required)
- Immutable audit log (blockchain-style chaining)

**🔐 Cryptographic Election:**

- Challenge-response with nonce signatures
- Proof-of-work for join requests (prevent DoS)
- Device fingerprinting (MAC + IP + TLS + hostname)

**🔐 Time Authority:**

- NTP consensus (3+ sources, reject outliers)
- Rough Time Protocol (RFC 8915) attestation
- Certificate-based time binding

**🔐 Hardware Security:**

- TPM 2.0 / Secure Enclave mandatory for pebble
- FIPS 140-2 compliant cryptography
- Hardware-backed key storage

**🔐 Advanced Monitoring:**

- SIEM integration (syslog, Splunk, ELK)
- Real-time anomaly detection
- Automated incident response
- Certificate transparency logs

**🔐 Partition Resilience:**

- Raft/Paxos consensus for joins
- Merkle tree state reconciliation
- Automatic conflict resolution
- Byzantine fault tolerance

**🔐 Compliance:**

- GDPR right-to-forget (PII purging)
- SOC2 audit trail immutability
- HIPAA encryption at rest (AES-256-GCM)
- PCI-DSS key rotation (30 days)

### Tier 2 Implementation Burden:

```yaml
Complexity: High
- Configuration: Complex (quorum setup, HSM provisioning)
- Maintenance: Ongoing (cert rotation, log aggregation, monitoring)
- User education: Extensive (admin training, runbooks)

Development Effort:
- Core hardening: 3 weeks
- Compliance features: 2 weeks
- Testing (security audit): 2 weeks
- Documentation: 1 week
- Total: 8 weeks for enterprise-grade security
```

---

## Activation Model

### Default Experience (Garden Pond):

```bash
# Initialize pond (Tier 1 - automatically)
$ garden-rake place pebble

✓ Pond initialized: My Garden
  Security tier: Garden (home lab)
  Encryption: ✓ Enabled
  Certificate lifetime: 1 hour
  Configuration changes: Warn + confirm

This pond is optimized for home labs.
For enterprise hardening: garden-rake harden pond

# User gets simple, working security
# No complex setup required
```

### Opt-In Hardening (Fortress Pond):

```bash
# Enable enterprise hardening
$ garden-rake harden pond

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

Learn more: garden-rake docs fortress-mode

# User makes explicit choice to accept complexity
```

---

## Recommended Minimal Viable Security (MVP)

**For Zen Garden 1.0 (Home Lab Focus):**

### Must-Have (P0 - Tier 1 Only):

1. ✅ Configuration safety net (warn → confirm → audit → rollback)
2. ✅ Encrypted join requests (Ed25519, asymmetric)
3. ✅ Short-lived certificates (1 hour, auto-renew)
4. ✅ Certificate CN binding (extract from mTLS)
5. ✅ Absolute timestamp validation (10 min window)
6. ✅ Used codes persistence (SQLite)
7. ✅ Visual security status (all commands show health)
8. ✅ Passphrase entropy check (20+ chars, zxcvbn)

**Effort:** 1 week  
**Complexity:** Low  
**Suitable for:** Home labs, small teams, dev environments

### Future Hardening (P1 - Tier 2):

1. ⏳ Multi-signature config approvals
2. ⏳ TPM/HSM support
3. ⏳ NTP consensus
4. ⏳ Quorum-based joins
5. ⏳ Advanced audit logging
6. ⏳ SIEM integration
7. ⏳ Compliance features (GDPR, SOC2, HIPAA)

**Effort:** 8 weeks  
**Complexity:** High  
**Suitable for:** Enterprises, regulated industries, security-critical deployments

---

## Revised Vulnerability Priorities

### Home Lab Reality Check:

| Vulnerability        | Tier 1 (Home) | Tier 2 (Enterprise) | Rationale                                              |
| -------------------- | ------------- | ------------------- | ------------------------------------------------------ |
| Config exploit       | ⚠️ MEDIUM     | 🔴 CRITICAL         | Home: Trust admin. Enterprise: Insider threat          |
| Predictable election | 🟢 LOW        | 🟡 HIGH             | Home: No targeted attacks. Enterprise: APT             |
| Time oracle          | 🟢 LOW        | 🔴 CRITICAL         | Home: Unlikely router compromise. Enterprise: Possible |
| MAC spoofing         | 🟡 MEDIUM     | 🟡 HIGH             | Both: Annoyance vs serious threat                      |
| Missing revocation   | 🟡 HIGH       | 🔴 CRITICAL         | Both: Need eviction, but urgency differs               |
| Pebble dictionary    | 🟡 MEDIUM     | 🔴 CRITICAL         | Home: Physical security. Enterprise: Data center       |
| Used codes desync    | 🟡 MEDIUM     | 🟡 HIGH             | Both: Edge case, timestamp mitigation sufficient       |
| Stone impersonation  | 🟡 HIGH       | 🔴 CRITICAL         | Both: Unacceptable, but impact varies                  |
| Join flood DoS       | 🟢 LOW        | 🟡 MEDIUM           | Home: mDNS scope .local. Enterprise: Multi-tenant      |
| Split-brain          | 🟢 LOW        | 🟡 HIGH             | Home: Manual fix OK. Enterprise: Auto-reconcile needed |
| Audit log tampering  | 🟢 LOW        | 🔴 CRITICAL         | Home: Trust users. Enterprise: Compliance requirement  |

### Tier 1 (Home Lab) - Simplified P0 List:

1. ✅ Configuration safety net (accidents, not attacks)
2. ✅ Short-lived certificates + auto-renew (simple revocation)
3. ✅ Certificate CN binding (prevent impersonation)
4. ✅ Absolute timestamp validation (desync protection)
5. ✅ Visual security feedback (user awareness)

**Total effort:** 4-5 days  
**Complexity:** Low  
**Security posture:** 7/10 (excellent for home labs)

### Tier 2 (Enterprise) - Full P0 List:

1. ✅ All Tier 1 features
2. 🔐 Multi-sig config approvals
3. 🔐 NTP consensus
4. 🔐 TPM/HSM mandatory
5. 🔐 Distributed audit logs
6. 🔐 Quorum-based operations
7. 🔐 Device fingerprinting
8. 🔐 Proof-of-work joins
9. 🔐 Advanced monitoring

**Total effort:** 7-8 weeks  
**Complexity:** High  
**Security posture:** 9.5/10 (production enterprise ready)

---

## Decision Framework

**For Zen Garden 1.0 MVP:**

```yaml
Recommendation: Implement Tier 1 (Garden Pond) ONLY

Reasoning:
  - Target audience: Home labs, hobbyists, small teams
  - Threat model: Trusted users, local network, physical security
  - UX priority: Frictionless > paranoid
  - Complexity budget: Simple setup, zero maintenance
  - Risk appetite: Accept home lab threat model

Benefits:
  - Ship in 1 week vs 2 months
  - Simple, understandable security
  - No complex configuration required
  - Clear upgrade path for enterprises (Tier 2 later)

Trade-offs:
  - Not suitable for regulated industries (initially)
  - Single admin trust model (acceptable for home)
  - Manual reconciliation on edge cases (rare)
  - No compliance certifications (add later)

Command: garden-rake place pebble # Simple, works out of box
```

**For Zen Garden 2.0 (Future):**

```yaml
Recommendation: Add Tier 2 (Fortress Pond) as opt-in

Timing: After initial adoption, user feedback, stability
Target: Enterprises requesting advanced security
Approach: Separate "hardening" phase, not MVP bloat
```

---

## Pragmatic Security Checklist

### Tier 1 (Garden Pond) - MVP:

**Week 1 (Security Foundations):**

- [ ] Day 1: Configuration safety net (warn, confirm, audit, rollback)
- [ ] Day 2: Short-lived certificates (1h lifetime, auto-renew every 30 min)
- [ ] Day 3: Certificate CN binding (extract from mTLS, ignore headers)
- [ ] Day 4: Absolute timestamp validation (10 min window, single NTP with Cornerstone fallback)
- [ ] Day 5: Visual security status (garden-rake status shows all health)

**Validation:**

- ✅ User can misconfigure and rollback easily
- ✅ Compromised Stone loses access in < 1 hour
- ✅ Stone impersonation prevented
- ✅ Network partition doesn't break joins (wider time window)
- ✅ User always knows security posture (visual feedback)

**Result:** Production-ready home lab security in 1 week

---

## Recommended Fixes (P1 - Before GA)

1. **Device fingerprinting** (MAC + IP + TLS fingerprint)
2. **Certificate Revocation List** (broadcast CRL updates)
3. **TPM/Secure Enclave** for pebble storage
4. **Passphrase entropy requirements** (20+ chars, zxcvbn)
5. **Partition detection** (heartbeat, read-only mode)
6. **Signed audit logs** (blockchain-style chaining)
7. **Pond-wide join rate limiting** (10/hour)

---

## Security Posture Assessment

### Before Fixes: 4/10 ❌

- Multiple critical vulnerabilities
- Single compromised Stone = full pond control
- Time-based attacks bypass all security
- No revocation mechanism
- Logs can be tampered

### After P0 Fixes: 7/10 ⚠️

- Critical vulnerabilities patched
- Configuration ACL prevents escalation
- Time attacks mitigated (NTP consensus)
- Short-lived certs limit compromise impact
- Identity spoofing prevented

### After P1 Fixes: 9/10 ✅

- Production-ready security posture
- Defense in depth (multiple layers)
- Tamper-evident audit logs
- Physical security (TPM)
- Network partition resilient

---

## Final Recommendation

**Current state:** DO NOT SHIP - Critical vulnerabilities present

**With P0 fixes:** PROCEED TO BETA - Acceptable for controlled environments

**With P1 fixes:** PRODUCTION READY - Suitable for public deployment

**Estimated effort:**

- P0 fixes: 5-7 days (1 developer)
- P1 fixes: 10-15 days (1 developer)
- Total: 3 weeks to production-grade security
