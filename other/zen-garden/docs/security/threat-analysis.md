# Threat Analysis

**Detailed attack scenarios and mitigations**

**Purpose**: Understand specific attack vectors and security defenses  
**Audience**: Security, Maintainer

---

## Contents

- [Vulnerability Matrix](#vulnerability-matrix)
- [Attack Scenarios](#attack-scenarios)
- [Cryptographic Weaknesses](#cryptographic-weaknesses)
- [Operational Risks](#operational-risks)
- [Residual Risks](#residual-risks)
- [Compliance Considerations](#compliance-considerations)

---

## Vulnerability Matrix

### Summary Table

| Vulnerability              | Tier 1    | Tier 2      | Status                        |
| -------------------------- | --------- | ----------- | ----------------------------- |
| Config propagation exploit | ⚠️ MEDIUM | 🔴 CRITICAL | P0 Fix: Safety net            |
| Predictable election       | 🟢 LOW    | 🟡 HIGH     | P0 Fix: Random salt           |
| Time oracle attack         | 🟢 LOW    | 🔴 CRITICAL | P0 Fix: Timestamp validation  |
| MAC spoofing               | 🟡 MEDIUM | 🟡 HIGH     | P1 Fix: Fingerprinting        |
| Missing cert revocation    | 🟡 HIGH   | 🔴 CRITICAL | P0 Fix: Short-lived certs     |
| Keystone dictionary attack   | 🟡 MEDIUM | 🔴 CRITICAL | P1 Fix: Passphrase entropy    |
| Used codes desync          | 🟡 MEDIUM | 🟡 HIGH     | P0 Fix: Timestamp + broadcast |
| Stone impersonation        | 🟡 HIGH   | 🔴 CRITICAL | P0 Fix: CN binding            |
| Join flood DoS             | 🟢 LOW    | 🟡 MEDIUM   | P1 Fix: Rate limiting         |
| Split-brain                | 🟢 LOW    | 🟡 HIGH     | P1 Fix: Partition detection   |
| Audit log tampering        | 🟢 LOW    | 🔴 CRITICAL | P1 Fix: Signed logs           |
| Bearer token replay        | 🟢 LOW    | 🟡 MEDIUM   | P0 Fix: Nonce tracking        |
| mDNS flooding              | 🟢 LOW    | 🟢 LOW      | P2 Fix: Signature filtering   |

**Reclassification rationale**: Home lab threat model (Tier 1) differs from enterprise (Tier 2). Trusted admin, physical security, and simplified recovery make many CRITICAL enterprise vulnerabilities LOW/MEDIUM for home labs.

---

## Attack Scenarios

### 1. Configuration Propagation Exploit

**Severity**: Medium (Tier 1), Critical (Tier 2)  
**Attack Vector**: Malicious administrator changes security config  
**Impact**: Weakened security, unauthorized access

**Scenario**:
```bash
# Attacker (with admin access) weakens rate limiting
garden-rake config set rate_limit_attempts 1000

# Enables brute-force attacks on TOTP codes
# All Stones receive config change via UDP broadcast
```

**Mitigation (P0)**:
```bash
# Safety net: Warn before security config changes
garden-rake config set rate_limit_attempts 1000

⚠️  WARNING: Changing security configuration
    Current: 5 attempts per hour
    New: 1000 attempts per hour
    Impact: Enables brute-force attacks

Confirm this change? [yes/NO]:

# Audit log entry created
audit: config_changed | key=rate_limit_attempts | old=5 | new=1000 | admin=user@stone-01
```

**Tier 2 enhancement**: Multi-admin approval required for security config changes.

---

### 2. Predictable Election Exploit

**Severity**: Low (Tier 1), High (Tier 2)  
**Attack Vector**: Attacker predicts which Stone will respond to join request  
**Impact**: Rogue Stone becomes Cornerstone

**Scenario**:
```
Attacker discovers election algorithm:
  SHA256(garden_name + stone_name + join_request_id)

Attacker crafts stone_name to win election:
  Tries: stone-zzz, stone-aaa, stone-000, etc.
  Finds: stone-zyx wins deterministic hash

Attacker deploys rogue Stone "stone-zyx"
New legitimate Stone joins, rogue Stone responds first
Rogue Stone issues certificate, intercepts all traffic
```

**Mitigation (P0)**:
```rust
// Add random salt to election hash
fn election_delay(stone_name: &str, request_id: &str) -> Duration {
    let salt = random_bytes(32);  // New: Random per-request
    let input = format!("{}{}{}", stone_name, request_id, hex::encode(salt));
    let hash = sha256(input.as_bytes());
    
    // Staggered delay: 0-10 seconds
    let delay_ms = (hash[0] as u64 * 40);  // 0-10,200ms range
    Duration::from_millis(delay_ms)
}
```

**Result**: Unpredictable election, attacker cannot reliably win.

---

### 3. Time Oracle Attack

**Severity**: Low (Tier 1), Critical (Tier 2)  
**Attack Vector**: Attacker manipulates NTP to replay TOTP codes  
**Impact**: Unauthorized Stone joins

**Scenario**:
```
Attacker captures valid TOTP code: KP7X9M (timestamp: 14:30:00)
Attacker compromises router, blocks NTP traffic
Attacker winds back local time to 14:30:00
Attacker replays captured code to join rogue Stone
```

**Mitigation (P0)**:
```rust
// Absolute timestamp validation
fn validate_totp(code: &str, timestamp: u64) -> Result<()> {
    // Reject codes older than 10 minutes (absolute time)
    if timestamp < now() - 600 {
        return Err(CodeExpired);
    }
    
    // Check if code already used (prevents replay)
    if is_code_used(code)? {
        audit_log(AuditEvent::DuplicateCode { code, timestamp });
        return Err(CodeAlreadyUsed);
    }
    
    // Mark code as used (persisted + broadcast)
    mark_code_used(code, timestamp)?;
    broadcast_code_used(code)?;
    
    Ok(())
}
```

**Result**: Time manipulation detected, replay attacks prevented.

**Tier 2 enhancement**: NTP consensus (multiple time sources, majority vote).

---

### 4. Stone Impersonation

**Severity**: High (Tier 1), Critical (Tier 2)  
**Attack Vector**: Attacker spoofs Stone identity in HTTP headers  
**Impact**: Unauthorized operations, privilege escalation

**Scenario**:
```bash
# Attacker sends request with forged Stone identity
curl -X POST https://stone-02:7185/api/operations/offer/mongodb \
  -H "X-Stone-Name: stone-01" \
  -H "Authorization: Bearer <valid-token>"

# Without CN binding, Stone-02 trusts X-Stone-Name header
# Attacker impersonates Cornerstone, issues fake certificates
```

**Mitigation (P0)**:
```rust
// Certificate CN binding (extract identity from mTLS)
fn verify_identity(req: &Request) -> Result<String> {
    // Extract CN from client certificate (trusted source)
    let cert = req.peer_certificate()?;
    let cn = cert.subject_common_name()?;
    
    // Ignore HTTP headers (untrusted)
    let header_name = req.headers().get("X-Stone-Name");
    
    // Detect mismatch
    if let Some(header) = header_name {
        if header != cn {
            audit_log(AuditEvent::IdentityMismatch {
                cert_cn: cn.clone(),
                header: header.clone(),
                source_ip: req.remote_addr(),
            });
            return Err(IdentityMismatch);
        }
    }
    
    Ok(cn)  // Use certificate CN (authoritative)
}
```

**Result**: Impersonation attempts detected and logged.

---

### 5. Keystone Dictionary Attack

**Severity**: Medium (Tier 1), Critical (Tier 2)  
**Attack Vector**: Attacker steals Keystone file, brute-forces weak passphrase  
**Impact**: CA compromise, full Garden takeover

**Scenario**:
```
Attacker gains physical access to Cornerstone
Copies /var/lib/zen-garden/keystone.enc
Runs dictionary attack offline:
  - rockyou.txt (14M common passwords)
  - Diceware wordlist permutations
  - Brute-force short passphrases

Weak passphrase "password123" cracked in 2 hours
Attacker extracts Pond CA private key
Attacker issues certificates to rogue Stones
```

**Mitigation (P1)**:
```bash
# Strong passphrase enforcement
garden-rake place keystone

Enter passphrase (20+ characters):
> password123

✗ Passphrase too weak
  Length: 11 characters (minimum 20 required)
  Strength: 1/4 (needs 3/4)
  Entropy: 25 bits (needs 77+ bits)
  
Recommendations:
  - Use diceware: 6+ random words
  - Generate: garden-rake generate-passphrase
  - Length: 20-30 characters minimum

Try again.
```

**Argon2id key derivation** (intentionally slow):
```yaml
Memory: 64MB (Tier 1), 256MB (Tier 2)
Iterations: 3 (Tier 1), 10 (Tier 2)
Time: ~500ms per attempt (Tier 1), ~2s (Tier 2)

Result:
  rockyou.txt: 14M passwords × 500ms = 81 days (Tier 1)
  With strong passphrase (77+ bits): ~centuries
```

**Tier 2 enhancement**: Hardware encryption (TPM 2.0), biometric unlock.

---

### 6. Split-Brain Scenario

**Severity**: Low (Tier 1), High (Tier 2)  
**Attack Vector**: Network partition creates two Cornerstones  
**Impact**: Inconsistent state, duplicate certificates

**Scenario**:
```
Garden: stone-01 (Cornerstone), stone-02, stone-03, stone-04
Network partition: {stone-01, stone-02} | {stone-03, stone-04}

Both partitions functional but isolated:
  Partition A: stone-01 (original Cornerstone)
  Partition B: stone-03 initiates election, becomes Cornerstone

New Stone joins Partition B:
  stone-05 → Receives certificate from stone-03

Network heals:
  Two Cornerstones exist (stone-01, stone-03)
  stone-05 has certificate from stone-03 (untrusted by Partition A)
```

**Mitigation (P1)**:
```rust
// Partition detection via heartbeat
fn detect_partition() -> Option<PartitionStatus> {
    let visible_stones = discover_stones();
    let total_stones = known_stones_count();
    
    let visibility = visible_stones.len() as f32 / total_stones as f32;
    
    if visibility < 0.5 {
        Some(PartitionStatus::MinorityPartition)
    } else if visibility < 0.8 {
        Some(PartitionStatus::DegradedNetwork)
    } else {
        None
    }
}

// Enter read-only mode during partition
if let Some(status) = detect_partition() {
    audit_log(AuditEvent::PartitionDetected { status });
    
    // Block write operations
    return Err(PartitionedReadOnly);
}
```

**Visual feedback**:
```bash
garden-rake status --security

⚠️  Network Partition Detected
    Visible Stones: 2/4 (50%)
    Status: Read-only mode
    Operations blocked: join, revoke, rotate
    
Check network connectivity: ping stone-03, ping stone-04
Partition will auto-recover when network heals.
```

**Tier 2 enhancement**: Quorum voting (2/3 majority required for operations).

---

## Cryptographic Weaknesses

### Ed25519 Key Size

**Assessment**: Strong (256-bit)  
**Quantum Resistance**: Vulnerable to Shor's algorithm  
**Timeline**: 10-15 years before practical quantum computers

**Migration path**: Post-quantum upgrade (NIST PQC algorithms) planned for Zen Garden 3.0.

### TOTP Algorithm

**Assessment**: Standard (RFC 6238)  
**Weaknesses**: Time-based (requires clock sync), 6-character limited entropy  
**Mitigations**: Short TTL (5 min), used-codes tracking, absolute timestamp validation

### Argon2id Parameters

**Tier 1**: Memory 64MB, Iterations 3, ~500ms per attempt  
**Tier 2**: Memory 256MB, Iterations 10, ~2s per attempt

**Assessment**: Adequate for home labs (Tier 1), strong for enterprise (Tier 2). Protects against GPU-accelerated attacks.

---

## Operational Risks

### Single Admin Trust Model

**Risk**: Administrator has complete control, can break security  
**Mitigation**: None (accepted risk for Tier 1)  
**Tier 2**: Multi-admin approval, separation of duties

### Keystone Backup

**Risk**: Keystone file loss → cannot add Stones or rotate CA  
**Mitigation**: Document backup procedures, test recovery  
**Recommendation**: Store encrypted backup off-site

### Certificate Auto-Renewal Failure

**Risk**: Cornerstone offline → Stones lose access within 1 hour  
**Mitigation**: Visual warnings, manual renewal, monitoring alerts  
**Recommendation**: High availability for Cornerstone (Tier 2)

---

## Residual Risks

### Accepted Risks (Tier 1)

1. **Physical access** - Attacker with physical access to Cornerstone can extract Keystone (if weak passphrase)
2. **Single admin abuse** - Trusted admin can break things (no multi-sig)
3. **Time manipulation** - Attacker controlling NTP can replay codes (wide ±10 min window)
4. **Network partition** - Temporary inconsistency during partition (read-only mode, manual recovery)

### Not Addressed (Out of Scope)

1. **Nation-state attacks** - Advanced persistent threats (APT)
2. **Zero-day exploits** - Undiscovered vulnerabilities in dependencies
3. **Supply chain attacks** - Compromised Docker images or OS packages
4. **Social engineering** - Tricking administrators into revealing passphrases

---

## Compliance Considerations

### GDPR (General Data Protection Regulation)

**Requirement**: Encryption of personal data  
**Zen Garden**: Pond provides encryption in transit (mTLS), but not at rest  
**Recommendation**: Enable full-disk encryption on Stones handling PII

### SOC 2 (Service Organization Control)

**Requirement**: Audit logging, access control, encryption  
**Zen Garden Tier 1**: Partial (local audit logs, basic access control)  
**Zen Garden Tier 2**: Full (distributed audit logs, multi-admin, TPM)

### HIPAA (Health Insurance Portability and Accountability Act)

**Requirement**: Encryption at rest and in transit, audit trails, access control  
**Zen Garden**: Not HIPAA-compliant (Tier 1 or Tier 2)  
**Reason**: Missing encryption at rest, insufficient audit detail  
**Recommendation**: Use Zen Garden for non-PHI data only

---

## Related Documentation

- **[Security Overview](overview.md)** - Threat models, security guarantees
- **[Pond Setup](pond-setup.md)** - Enable Pond authentication
- **[Security Specification](../specs/security.md)** - Complete technical design
- **[Maintainer Docs](../ops/maintainers.md)** - Operational security

---

**Last Updated**: 2026-01-18
