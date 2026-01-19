---
status: Accepted
date: 2026-01-15
---

# SECURITY-0001: Pond Two-Tier Security Model

## Status

**Accepted** - Tier 1 (Garden Pond) implemented, Tier 2 (Deep Pond) planned Q3-Q4 2026

## Context

Zen Garden serves two distinct security environments:

1. **Home Lab (Tier 1)**: Solo admin or small trusted team, 2-10 Stones, physical security assumed, compliance not required
2. **Enterprise (Tier 2)**: Multiple admins (potentially untrusted), 10+ Stones, compliance requirements (GDPR, SOC2, HIPAA), insider threat mitigation required

**Problem:** Single security model cannot satisfy both home lab simplicity AND enterprise compliance without over-engineering home labs or under-securing enterprises.

**Constraints:**
- Tier 1 must remain frictionless (zero config default, opt-in security)
- Tier 2 must satisfy security auditors (audit logs, multi-sig, defense in depth)
- Common foundation (don't build two separate systems)

**Source:** [SECURITY-SPEC.md § Security Tiers](../SECURITY-SPEC.md#security-tiers)

## Decision

Implement **two-tier security model** with progressive hardening:

### Tier 1: Garden Pond (Home Lab)

**Threat model:**
- Realistic threats: Network sniffing, user mistakes, physical theft (weak passphrase)
- NOT addressed: Insider attacks (single admin trusted), nation-state adversaries

**Features:**
- mTLS between Stones (prevents network sniffing)
- Bluetooth pairing model for Stone admission (TOTP codes, physical proximity)
- Passphrase-encrypted Pond CA (Keystone) protects against physical theft
- Single admin trust model (no multi-signature)

**Philosophy:** "Set your stones, make sure everything is working, **fill the pond**."

Security is **opt-in** after initial setup. Start frictionless, add hardening when needed.

---

### Tier 2: Deep Pond (Enterprise)

**Threat model:**
- Additional threats: Insider attacks, lateral movement, compliance violations, APT attacks
- Accepted risks: Nation-state adversaries (defense-in-depth recommended), zero-day exploits

**Features (beyond Tier 1):**
- Multi-signature operations (add Stone, drain pond requires 2+ admin signatures)
- Audit logging (all operations recorded, tamper-evident logs)
- Role-based access control (admin vs operator vs viewer roles)
- Network segmentation (Stones in security zones, firewall rules)
- Attestation and compliance reporting (SOC2, HIPAA audit trails)

**Status:** Planned Phase 2 (Q3-Q4 2026), design in progress

---

### Common Foundation

Both tiers share:
- Two-keypair architecture (Stone identity + Pond CA)
- Certificate-based authentication (mTLS)
- Distributed CA model (no single point of failure)
- Bluetooth pairing UX (TOTP admission codes)

## Consequences

### Positive

✅ **Home labs not over-engineered:** Tier 1 simple, no unnecessary complexity  
✅ **Enterprises properly secured:** Tier 2 addresses compliance, insider threats  
✅ **Clear upgrade path:** Users start Tier 1, upgrade to Tier 2 when needed  
✅ **Common codebase:** Most code shared, reduces maintenance burden  
✅ **Security team approval:** Rating 9.5/10 for Bluetooth pairing model

### Negative

❌ **Two security models to maintain:** Tier 1 vs Tier 2 feature parity complexity  
❌ **Documentation burden:** Must explain when to use each tier  
❌ **Testing complexity:** Must validate both threat models  
❌ **Potential confusion:** Users may not understand tier difference

### Risks

**Risk:** Users deploy Tier 1 in enterprise environments (insufficient security)  
**Mitigation:** Documentation clearly states Tier 1 = home lab, Tier 2 = enterprise. Compliance warnings.

**Risk:** Tier 2 implementation delayed, enterprises blocked on adoption  
**Mitigation:** Tier 1 + external security hardening (firewall, VPN) acceptable interim solution

**Risk:** Tier 2 so complex that adoption suffers  
**Mitigation:** Multi-sig only for sensitive operations (add Stone, drain pond), routine ops remain single-sig

## Alternatives Considered

### Alternative 1: Single Security Model (Enterprise-Grade for All)

**Approach:** Multi-sig, audit logs, RBAC for everyone (home labs included)

**Why not:**
- Massive friction for home lab users (primary audience)
- Forces complexity on users who don't need it
- Violates "frictionless by default" design principle

### Alternative 2: No Security Model (Trust Network)

**Approach:** Assume trusted LAN, no authentication/encryption

**Why not:**
- Unacceptable risk even for home labs (network sniffing trivial)
- Zero enterprise adoption (compliance failure)
- Physical theft scenarios leave data exposed

### Alternative 3: Plugin-Based Security (Bring Your Own Auth)

**Approach:** Zen Garden provides hooks, users integrate OAuth/LDAP/SAML

**Why not:**
- Massive integration burden on users
- No default security (back to "no security" problem)
- Different plugins = fragmented ecosystem, interop issues

## Implementation Notes

### Tier 1 Architecture (Two-Keypair Model)

**Stone Identity Keypair** (unique per Stone):
- Private: `/var/lib/zen-garden/stone-XX.key` (Ed25519)
- Purpose: Sign bearer tokens, encrypt Keystone, generate TOTP codes
- Scope: Never leaves Stone

**Pond CA Keypair** (shared across pond):
- Stored as: `/var/lib/zen-garden/keystone.enc` (encrypted with passphrase + Stone identity key)
- Purpose: Issue certificates to new Stones joining pond
- Distribution: Replicated to all Stones in pond (encrypted per-Stone)

**Bluetooth Pairing Flow:**
```
1. Operator on Cornerstone: garden-rake invite stone-02
   → Generates TOTP code: 836294 (30s validity)

2. Operator on stone-02: garden-rake pond join --code 836294
   → Sends join request with TOTP

3. Cornerstone validates TOTP, issues certificate
   → stone-02 receives cert, stores at /var/lib/zen-garden/stone-02.pem

4. stone-02 announces Pond membership
   → mDNS TXT: pond=active, fingerprint=abc123
```

### Tier 2 Extensions (Planned)

**Multi-Signature Operations:**
```bash
# Operation requires 2 of 3 admins to approve
garden-rake pond add-stone stone-04 --require-signatures 2/3

# Admin 1 approves
garden-rake pond approve op_123 --admin admin1 --sign

# Admin 2 approves (operation executes)
garden-rake pond approve op_123 --admin admin2 --sign
```

**Audit Logging:**
- All operations logged to tamper-evident log (append-only, signed)
- Log entries: timestamp, operator, operation, target, result
- Compliance export: `garden-rake audit export --since 2026-01-01 --format json`

## References

- **Canonical spec:** [SECURITY-SPEC.md](../SECURITY-SPEC.md)
- **Threat models:** [SECURITY-SPEC.md § Threat Models](../SECURITY-SPEC.md#threat-models)
- **Pond architecture:** [SECURITY-SPEC.md § Pond Security Architecture](../SECURITY-SPEC.md#pond-security-architecture)
- **Cryptographic design:** [SECURITY-SPEC.md § Cryptographic Design](../SECURITY-SPEC.md#cryptographic-design)
- **TOTP admission:** [proposals/TOTP-STONE-ADMISSION.md](../proposals/TOTP-STONE-ADMISSION.md)

## Security Review

**Reviewed by:** Security team (2026-01-15)  
**Rating:** 9.5/10 for Bluetooth pairing model  
**Concerns:** None for Tier 1, Tier 2 multi-sig implementation requires additional review

**Threat model validation:**
- Tier 1 adequately addresses home lab threats ✅
- Tier 2 adequately addresses enterprise threats ✅ (pending implementation review)
- Nation-state adversaries explicitly out of scope ✅ (documented, accepted)

## Versioning

**Tier 1 stability:** Pond API stable for v1.x series (certificate format, TOTP protocol)

**Tier 2 timeline:**
- Design: Q2 2026
- Implementation: Q3 2026
- Security audit: Q4 2026
- Production release: v2.0 (Q4 2026)
