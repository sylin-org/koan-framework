# Security Model

**When and how to add Pond security.**

---

## Threat Model

### Baseline: mDNS Only (No Security)

**Protections:**
- None (plaintext discovery, plaintext connections)

**Threats NOT addressed:**
- ❌ Rogue service announcement (malicious device claims "I'm MongoDB")
- ❌ Network sniffing (credentials visible on wire)
- ❌ Man-in-the-middle attacks (no certificate validation)
- ❌ Service impersonation (attacker replaces legitimate Stone)

**Acceptable for:**
- ✅ Home lab on trusted LAN
- ✅ Educational environments (non-sensitive data)
- ✅ Development/staging (test data only)
- ✅ Strong physical security (locked office, access control)

**Not acceptable for:**
- ❌ Production workloads
- ❌ Sensitive data (PII, financial records, health data)
- ❌ Compliance requirements (GDPR, HIPAA, PCI-DSS)
- ❌ Untrusted networks (guest WiFi, shared spaces)

---

## Pond: Cryptographic Binding

**Adds mTLS (mutual TLS) with certificate pinning.**

### What Pond Provides

**Service authentication:**
- Stones receive certificates during binding
- Announcements include certificate fingerprint (TXT record)
- Apps validate certificate before connecting
- Rogue devices cannot announce without valid certificate

**Connection encryption:**
- All traffic encrypted via TLS 1.3
- Credentials never plaintext on wire
- Protection against network sniffing

**Man-in-the-middle prevention:**
- Certificate pinning (trust specific cert, not CA)
- No way to intercept and proxy connections
- Stone identity bound to cryptographic proof

### What Pond Does NOT Provide

**Not zero-trust:**
- Once inside Pond, all Stones trusted equally
- No per-service authorization policies
- No attribute-based access control (ABAC)

**Not intrusion detection:**
- No anomaly detection
- No traffic inspection
- No breach notification

**Not compliance certification:**
- No SOC 2, ISO 27001, FedRAMP
- DIY security (you manage certificates)
- Audit logs minimal

**Use case:** Stronger security for small-scale self-hosted infrastructure. Not enterprise-grade access control.

---

## When to Use Pond

### Scenarios Requiring Pond

**1. Production workloads**
```
Examples:
- Customer database for small business
- Financial records for accounting firm
- Medical records for clinic
- CRM data for sales team
```

**2. Sensitive data**
```
Triggers:
- Personal identifiable information (PII)
- Payment card data
- Protected health information (PHI)
- Proprietary business data
```

**3. Compliance requirements**
```
Regulations:
- GDPR (EU data protection)
- HIPAA (US healthcare)
- PCI-DSS (payment cards)
- CCPA (California privacy)
```

**4. Untrusted networks**
```
Examples:
- Guest WiFi at coworking space
- Shared office building network
- Public venue with device access
- Multi-tenant data center
```

### Scenarios NOT Requiring Pond

**1. Home lab (trusted LAN)**
```
Conditions:
- WPA2/WPA3 WiFi encryption
- Known devices only
- Physical access control
- Non-sensitive data
```

**2. Educational environments**
```
Conditions:
- Test/demo data only
- Short-lived projects
- Learning focus (not production)
- Acceptable data loss risk
```

**3. Development/staging**
```
Conditions:
- Non-production data
- Isolated network segment
- Ephemeral infrastructure
- Rapid iteration priority
```

---

## How Pond Works

### Architecture

```
Garden (Pond-enabled)
├─ pond-ca (Certificate Authority)
│  ├─ Issues certificates to Stones
│  └─ Never signs external requests
│
├─ stone-01 (MongoDB)
│  ├─ Certificate: CN=stone-01.zen-garden
│  └─ Announces: TXT fingerprint=sha256:abc123...
│
├─ stone-02 (Redis)
│  ├─ Certificate: CN=stone-02.zen-garden
│  └─ Announces: TXT fingerprint=sha256:def456...
│
└─ app (Client)
   ├─ Discovers: stone-01, fingerprint=sha256:abc123...
   └─ Validates: Certificate matches fingerprint before connecting
```

### Setup Process

**1. Initialize Pond**
```bash
# Creates certificate authority
garden-rake init --pond

# Output:
# [pond] created CA certificate
# [pond] CA fingerprint: sha256:abc123def456...
# [pond] store CA cert securely (required for new Stones)
```

**2. Bind each Stone**
```bash
# Stone requests certificate
garden-rake bind stone-01

# Pond CA issues certificate
# [pond] issued certificate: CN=stone-01.zen-garden
# [pond] fingerprint: sha256:xyz789...
# [pond] certificate expires: 2027-01-15
```

**3. Stones announce with fingerprint**
```
mDNS announcement:
  Service: _koan-stone._tcp.local.
  TXT: offering=mongodb, fingerprint=sha256:xyz789...
```

**4. Apps validate before connecting**
```javascript
// Discovery returns fingerprint
const service = await discover('mongodb');
// { uri: 'mongodb://stone-01:27017', fingerprint: 'sha256:xyz789...' }

// Connection validates certificate
const client = new MongoClient(service.uri, {
  tls: true,
  tlsCertificateFile: service.fingerprint // Pinned certificate
});
```

### Certificate Management

**Rotation (recommended: annually)**
```bash
# Renew Stone certificate
garden-rake renew stone-01

# Stone announces new fingerprint
# Apps reconnect with new certificate
```

**Revocation (compromised Stone)**
```bash
# Revoke certificate
garden-rake revoke stone-01

# Stone cannot announce with invalid cert
# Apps reject connections (fingerprint mismatch)
```

**CA compromise (nuclear option)**
```bash
# Reinitialize Pond (new CA)
garden-rake init --pond --force

# Rebind ALL Stones
garden-rake bind stone-01
garden-rake bind stone-02
# ...

# Update apps with new CA trust
```

---

## Implementation Status

**Current phase:** Design and specification

**Planned timeline:**
- **Phase 1 (Q1 2026):** Basic mTLS implementation
  - Self-signed certificates
  - Manual binding process
  - Certificate pinning in apps
  
- **Phase 2 (Q2 2026):** Automated certificate rotation
  - Expiration warnings
  - One-command renewal
  - Zero-downtime rotation
  
- **Phase 3 (Q3 2026):** Advanced security features
  - Per-service authorization policies
  - Audit logging
  - Anomaly detection (basic)

**Note:** Specialist assessment recommends moving basic mTLS to Phase 1 (not deferred to Phase 3). This timeline reflects that feedback.

---

## Trade-offs

### Complexity vs. Security

**Without Pond:**
- ✅ Simple (no certificate management)
- ✅ Fast iteration (no crypto overhead)
- ❌ Vulnerable (plaintext everything)

**With Pond:**
- ✅ Secure (encrypted + authenticated)
- ❌ Complex (certificate lifecycle)
- ❌ Slower setup (binding process)

**Recommendation:** Start without Pond for learning/development. Add Pond when moving to production or sensitive data.

### Performance Impact

**Encryption overhead:**
- CPU: +5-15% (TLS handshake + symmetric encryption)
- Latency: +2-5ms (certificate validation)
- Throughput: Minimal impact on bulk data (hardware AES acceleration)

**Discovery overhead:**
- TXT records larger (fingerprint adds ~80 bytes)
- No impact on mDNS timing (fits in single packet)

**For most use cases:** Performance impact negligible. Database/app logic dominates, not encryption.

---

## Best Practices

**1. Certificate storage**
```bash
# Store CA cert securely
chmod 600 /etc/zen-garden/ca-cert.pem
chown root:root /etc/zen-garden/ca-cert.pem

# Backup CA cert (required for disaster recovery)
scp /etc/zen-garden/ca-cert.pem backup-host:/secure/path/
```

**2. Regular rotation**
```bash
# Annual renewal (cronjob)
0 0 1 1 * garden-rake renew stone-01
```

**3. Monitoring**
```bash
# Check certificate expiration
garden-rake status --pond

# Output:
# stone-01: expires 2027-01-15 (365 days)
# stone-02: expires 2026-03-20 (64 days) ⚠️ expiring soon
```

**4. Defense in depth**
```
Pond security + other layers:
├─ Network segmentation (VLAN isolation)
├─ Firewall rules (restrict port access)
├─ Application authentication (app-level auth)
└─ Encryption at rest (disk encryption)
```

Pond is one layer, not sole security mechanism.

---

## Limitations

**Not a substitute for:**
- Proper firewall configuration
- Application-level authentication
- Database access controls (users/permissions)
- Regular security audits
- Incident response planning

**Pond secures discovery and transport. Application security remains your responsibility.**

---

## Further Reading

- [Understanding](UNDERSTANDING.md) - Core concepts (Stones, Lantern, Pond)
- [Getting Started](GETTING-STARTED.md) - Quick setup (no Pond)
- [Technical Reference](REFERENCE.md) - TXT record format, API details
- [Roadmap](ROADMAP.md) - Pond implementation timeline


