# Security Overview

**Threat models, security guarantees, and when to use Pond**

**Purpose**: Understand Zen Garden's security model and what it protects  
**Audience**: Security, Visitor, Operator

---

## Contents

- [Security Philosophy](#security-philosophy)
- [Threat Models](#threat-models)
- [Pond Security](#pond-security)
- [What Pond Protects](#what-pond-protects)
- [What Pond Does NOT Protect](#what-pond-does-not-protect)
- [Security Tiers](#security-tiers)
- [When to Use Pond](#when-to-use-pond)

---

## Security Philosophy

**Pragmatic, not paranoid** - Security should protect against realistic threats, not theoretical ones. Home labs face different risks than enterprises.

**Visible, not hidden** - Users should understand security status at a glance. No security through obscurity.

**Recoverable, not fragile** - When issues occur, clear remediation paths. No unrecoverable states.

**Frictionless by default** - Zero configuration for common use cases. Security shouldn't require a PhD.

### Design Principle

> "Security that requires reading documentation is security that won't be used. Make it visible, understandable, and recoverable."

Every security feature includes:
1. **Prevention** (Technical) - Cryptographic/protocol-level security
2. **Detection** (Monitoring) - Visual feedback when issues occur
3. **Recovery** (User-Facing) - Clear steps to fix problems

---

## Threat Models

### Home Lab Reality (Tier 1 Target)

**Environment**:
- Solo admin or small trusted team
- 2-10 Stones on local network
- Physical security assumed (home/office)
- Trusted users (family, colleagues)

**Realistic Threats**:

| Threat           | Likelihood | Impact | Mitigation Priority              |
| ---------------- | ---------- | ------ | -------------------------------- |
| User mistakes    | HIGH       | Medium | **P0** - Safety nets, rollback   |
| Network sniffing | MEDIUM     | Medium | **P0** - Encryption              |
| Physical theft   | LOW        | High   | **P1** - Passphrase + encryption |
| Malware on Stone | LOW        | High   | **P1** - Isolation, monitoring   |
| Insider attack   | VERY LOW   | High   | **P2** - Audit logs              |
| Nation-state     | NEGLIGIBLE | N/A    | Not addressed                    |

**Accepted Risks**:
- Single admin can break things (trust model)
- Keystone extractable with physical access + weak passphrase
- Network partition may cause temporary inconsistency
- Time manipulation possible if router compromised

**Philosophy**: Protect against accidents and common attacks, not nation-states.

### Enterprise Reality (Tier 2 Target)

**Environment**:
- Multiple administrators (untrusted)
- 10+ Stones, potentially multi-tenant
- Compliance requirements (GDPR, SOC2, HIPAA)
- Hostile network possible

**Additional Threats**:

| Threat               | Likelihood | Impact   | Mitigation Priority         |
| -------------------- | ---------- | -------- | --------------------------- |
| Insider threat       | HIGH       | Critical | **P0** - Multi-sig, audit   |
| APT attacks          | MEDIUM     | Critical | **P0** - Defense in depth   |
| Lateral movement     | MEDIUM     | Critical | **P0** - Segmentation       |
| Compliance violation | HIGH       | Critical | **P0** - Audit, encryption  |
| Supply chain         | LOW        | Critical | **P1** - Attestation        |
| Data breach          | MEDIUM     | Critical | **P0** - Encryption at rest |

**Philosophy**: Defense in depth, compliance-first, insider threat mitigation.

---

## Pond Security

**Pond** - Optional security layer connecting Stones with mutual TLS (mTLS) authentication.

**Philosophy**: "Set your stones, make sure everything is working, fill the pond."

Users start without Pond (frictionless), then enable security when ready:
```bash
garden-rake place keystone
```

### Bluetooth Pairing Model

Inspired by Bluetooth device pairing - familiar UX, proven security model.

```
Bluetooth Pairing          →  Pond Join
─────────────────────────────────────────
1. Put device in pairing mode  →  garden-rake invite stone
2. Shows 6-digit code          →  Displays TOTP code locally
3. Type code on other device   →  Type code on new Stone
4. Devices paired              →  Stone joined pond
```

**Security team rating**: 9.5/10

### Components

**Keystone** - Encrypted file containing Pond CA (certificate authority) keypair. Cornerstone holds the Keystone and uses it to issue certificates to joining Stones.

**Cornerstone** - First Stone in a Pond with certificate authority. Only one Cornerstone per Pond. Issues certificates during Stone admission.

**mTLS Certificates** - Each Stone receives a short-lived certificate (1 hour TTL) for authentication. Auto-renewed every 30 minutes.

**TOTP Codes** - Time-based one-time passwords for Stone admission. 6 characters, 5-minute TTL, validated locally (never transmitted).

---

## What Pond Protects

### ✅ Protection Provided

**1. Authentication** - Verify Stone identity (prevent rogue devices)
```
Without Pond: Any device can announce "I offer MongoDB"
With Pond: Only Stones with valid certificates trusted
```

**2. Encryption** - Protect traffic from network sniffing
```
Without Pond: mDNS announcements plaintext, HTTP traffic visible
With Pond: mTLS encryption for all inter-Stone communication
```

**3. Authorization** - Control which Stones can join Garden
```
Without Pond: Any device on network can join automatically
With Pond: Administrator approves each Stone (TOTP code)
```

**4. Tamper Detection** - Detect certificate mismatch or manipulation
```
Certificate CN binding: Identity extracted from mTLS, not headers
Mismatch triggers alert + audit log entry
```

**5. Visual Security Status** - Understand security posture at a glance
```bash
garden-rake status --security

Pond Status: Active (Garden Pond - Tier 1)
Cornerstone: stone-01
Stones: 4 joined, 0 pending
Certificates: All valid, expires in 28 min (auto-renew at 30 min)
Last Audit: 2 events (last 24h) - view with 'audit show'
```

---

## What Pond Does NOT Protect

### ❌ Not Protected

**1. Physical Access** - If attacker has physical access to Cornerstone:
```
Risk: Extract Keystone file, brute-force weak passphrase
Mitigation: Strong passphrase (20+ chars), hardware encryption (Tier 2)
```

**2. Time Manipulation** - If attacker controls network time (NTP):
```
Risk: TOTP replay attacks (valid codes reused)
Mitigation: Used codes tracking (Tier 1), NTP consensus (Tier 2)
```

**3. Malware on Stone** - If attacker compromises Stone OS:
```
Risk: Extract private key, issue fake certificates
Mitigation: OS hardening, container isolation, TPM (Tier 2)
```

**4. Network Partition** - If network splits (split-brain):
```
Risk: Temporary inconsistency, duplicate Cornerstones possible
Mitigation: Read-only mode during partition, quorum voting (Tier 2)
```

**5. Nation-State Attacks** - Advanced persistent threats (APT):
```
Scope: Out of scope for Tier 1 (home lab focus)
Consideration: Tier 2 adds defense in depth, but not APT-proof
```

---

## Security Tiers

### Tier 1: Garden Pond (Default)

**Target**: Home labs, small teams (2-10 Stones)  
**Complexity**: Low (zero config)  
**Effort**: 1 week implementation  
**Security Rating**: 7/10  
**Philosophy**: Frictionless, protect against accidents

**Features**:
- Short-lived certificates (1 hour TTL, auto-renew)
- TOTP admission (6-character codes, 5-minute TTL)
- Encrypted join requests (Ed25519)
- Visual security feedback
- Local audit logs (SQLite, 30-day retention)
- Simple time sync (single NTP source)

**Suitable for**:
- Home labs with trusted users
- Development environments
- Small teams (2-5 people)
- Non-sensitive data
- Physical security assumed

→ See: [pond-setup.md](pond-setup.md)

**CLI**: `garden-rake place keystone`

### Tier 2: Deep Pond (Enterprise)

**Target**: Enterprise, compliance requirements  
**Complexity**: High (TPM, quorum, multi-admin)  
**Effort**: 8 weeks implementation  
**Security Rating**: 9.5/10  
**Philosophy**: Defense in depth, compliance-first

**Additional Features** (beyond Tier 1):
- Hardware security (TPM 2.0 required)
- Multi-signature approvals (3+ admins)
- NTP consensus (multiple time sources)
- Partition quorum enforcement
- Advanced audit logging (distributed, immutable)
- Certificate pinning
- Rate limiting with fingerprinting
- Keystone rotation protocols

**Suitable for**:
- Enterprises (10+ Stones)
- Compliance needs (GDPR, SOC2, HIPAA)
- Multi-tenant environments
- Sensitive data (PII, financial, medical)
- Untrusted networks

**CLI**: `garden-rake place keystone deep`

→ See: [../specs/security.md](../specs/security.md) (full specification)

---

## When to Use Pond

### Use Pond When...

✅ **Running production workloads** - Sensitive data, uptime requirements  
✅ **On untrusted networks** - Public WiFi, shared office networks  
✅ **Multiple administrators** - Need access control and audit trails  
✅ **Compliance requirements** - GDPR, SOC2, HIPAA mandates encryption  
✅ **Rogue device risk** - Open network where unknown devices can join

### Don't Need Pond When...

❌ **Home lab experimentation** - Learning, testing, non-sensitive data  
❌ **Strong physical security** - Locked office, trusted users only  
❌ **Same local network** - All devices on home LAN with firewall  
❌ **Solo administrator** - You control all Stones, trust yourself  
❌ **Non-sensitive data** - Test databases, development environments

### Migration Path

**Start simple, add security when needed**:
1. Deploy Stones without Pond (get familiar)
2. Validate discovery and services working
3. Enable Pond when ready: `garden-rake place keystone`
4. Existing services continue (zero downtime)
5. New operations require certificates (automatic)

---

## Related Documentation

- **[Pond Setup Guide](pond-setup.md)** - Enable Pond, join Stones
- **[Threat Analysis](threat-analysis.md)** - Detailed attack scenarios
- **[Security Specification](../specs/security.md)** - Complete technical design
- **[TOTP Admission Proposal](../proposals/totp-admission.md)** - Stone admission workflow

---

**Last Updated**: 2026-01-18
