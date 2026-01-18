# Security Team Review: Pond Security Model

**Date:** January 15, 2026  
**Reviewers:** Security Architecture Team  
**Status:** Design Review

---

## Proposed User Experience

```bash
# Enable pond security (garden-wide)
$ garden-rake place pebble ThisIsMyPassPhrase
✓ Pond initialized with master key
✓ All stones now require authentication

# Add new stone to secure pond
$ garden-rake invite stone
Invitation code: AJRE (valid for 5 minutes)

# On new stone
$ garden-rake place stone AJRE
✓ Stone joined secure pond
✓ Pebble installed

# Disable pond security
$ garden-rake remove pebble ThisIsMyPassPhrase
⚠ Warning: This will make your garden open again
Confirm (yes/no): yes
✓ Pond disabled, all stones now open
```

---

## Security Specialist Team Assessment

### Threat Model Analyst

**Overall Assessment:** 7/10 - Good UX, needs cryptographic refinement

#### Threat Scenarios

**Scenario 1: Network Eavesdropping**

```
Attacker on same LAN sniffs traffic:
- mDNS announcements (public, expected)
- HTTP API requests (currently unencrypted)
- Stone responses (currently unencrypted)

With Pond:
- mDNS announcements include cert fingerprint (public, OK)
- HTTP API requests use mTLS (encrypted, authenticated)
- Attacker cannot impersonate Stone or Rake
```

**Verdict:** ✅ Pond mitigates

**Scenario 2: Rogue Stone Announcement**

```
Attacker announces fake MongoDB Stone:
  offering=mongodb
  port=27017
  host=attacker.local

App resolves zen-garden:mongodb → attacker.local
App sends credentials to attacker

With Pond:
- Stone announcement includes cert fingerprint
- App validates cert against pond CA
- Invalid cert → connection refused
```

**Verdict:** ✅ Pond mitigates

**Scenario 3: Stolen Passphrase**

```
Attacker observes user typing "ThisIsMyPassPhrase"
Attacker runs: garden-rake remove pebble ThisIsMyPassPhrase

Without rate limiting:
- Attacker disables pond security
- Garden becomes open
```

**Verdict:** ⚠️ Needs rate limiting + audit log

**Scenario 4: Invitation Code Interception (REVISED)**

**Original Attack:**

```
User runs: garden-rake invite stone → AJRE
Attacker on network sees code (mDNS? HTTP?)
Attacker runs: garden-rake place stone AJRE
Attacker's stone joins pond
```

**Revised Proposal (Time-Based + Asymmetric Encryption):**

```
User runs: garden-rake invite stone
→ Code: AJRE (calculated from cornerstone pebble + current time, not transmitted)

Attacker sees: No code transmission (displayed locally only)

New Stone: garden-rake place stone AJRE
→ Encrypts AJRE with cornerstone's public key
→ Sends encrypted payload to cornerstone
→ Cornerstone decrypts and validates:
  1. Code matches TOTP(pebble_secret + time, ±5 min window)
  2. Code not used before
  3. Under rate limit

Attacker cannot:
- Intercept code (never on network)
- Replay code (one-time use)
- Brute force remotely (encrypted with cornerstone pubkey)
```

**Security Analysis:**

✅ **Improvements:**

- Code never transmitted unencrypted
- Asymmetric encryption prevents network sniffing
- Time-based codes auto-expire (5 min window)
- Cornerstone validates without storing codes in advance

⚠️ **Remaining Concerns:**

- Clock synchronization (what if new Stone's clock is wrong?)
- Cornerstone concept = single point of failure (what if cornerstone offline?)
- User must physically access cornerstone to generate code
- 4 characters still vulnerable to local brute force (if attacker has physical access)

**Verdict:** ✅ **Significant improvement** - Network interception mitigated

**Additional Recommendations:**

1. Use NTP for clock synchronization (validate before join)
2. Allow any pond Stone to act as cornerstone (replica model)
3. Increase to 6 characters for local brute force protection
4. Add device fingerprinting (prevent attacker from generating valid request)

**Scenario 5: Passphrase Brute Force**

```
Attacker tries: garden-rake remove pebble <guess>
Common passwords: "password", "123456", "garden"

Without rate limiting:
- Attacker automates attempts
- Weak passphrase cracked in minutes
```

**Verdict:** ⚠️ Needs:

- Minimum passphrase strength (12+ chars)
- Rate limiting (exponential backoff)
- Audit log (failed attempts)

#### Recommendations (Priority)

**P0 - Critical (REVISED):**

1. **Passphrase strength enforcement** (min 12 chars, no common passwords)
2. **Time-based invitation codes** (TOTP with ±5 min window)
3. **Asymmetric encryption** (encrypt join request with cornerstone pubkey)
4. **One-time code use** (track used codes in persistent storage)
5. **Clock synchronization check** (warn if drift > 5 min, attempt NTP sync)
6. **mTLS for all Moss HTTP APIs** (not just service traffic)

**P1 - High:**

1. **Audit logging** (join attempts\*\* (max 10 attempts per source IP)
2. **Cornerstone replica** (allow any pond Stone to validate invitations, not just original)
3. **Revocation mechanism** (`garden-rake revoke stone stone-01`)
4. **Used codes persistence** (SQLite or file-based storage, survives restartisplay locally only)
5. **Revocation mechanism** (`garden-rake revoke stone stone-01`)

**P2 - Medium:**

1. **Hardware security module support** (store master key in TPM/Keyring)
2. **Passphrase rotation** (`garden-rake rotate pebble`)
3. **Stone attestation** (prove Stone is genuine Moss installation)

---

### Cryptography Architect

**Overall Assessment:** 6/10 - Concept sound, implementation details missing

#### Cryptographic Design

**Current Proposal Gaps:**

1. ❓ How is passphrase converted to master key? (PBKDF2? Argon2?)
2. ❓ What key material is stored in pebble? (symmetric? asymmetric?)
3. ❓ How do Stones authenticate each other? (mTLS? HMAC?)
4. ❓ How is invitation code generated? (CSPRNG? time-based?)
5. ❓ Where is master key stored? (filesystem? encrypted?)

#### Proposed Cryptographic Architecture

**Key Hierarchy:**

```
User Passphrase
      ↓ (Argon2id)
  Master Key (256-bit)
      ↓ (HKDF)
  ├─ Pond CA Private Key (Ed25519)
  ├─ Stone Certificate Signing Key
  └─ Invitation HMAC Key
```

**Pebble Structure:**

```
Pebble (per Stone):
- Pond ID (UUID)
- Stone Certificate (Ed25519 public key)
- Stone Private Key (Ed25519, encrypted with master key)
- CA Certificate (Pond CA public key)
- Metadata (stone name, join timestamp)

Storage: /var/lib/zen-garden/pebble.enc (AES-256-GCM)
Size: ~4KB (not 4MB - old spec was excessive)
```

**Invitation Code Generation (REVISED - Time-Based):**

````rust
fn generate_invitation_code(
    pebble_secret: &[u8],
    cornerstone_keypair: &Ed25519KeyPair
) -> (String, u64) {
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_secs();

    // 5-minute window (300 seconds)
    let time_window = timestamp / 300;

    // TOTP-style code generation
    let payload = format!("{}:{}", hex::encode(pebble_secret), time_window);
    let hmac = hmac_sha256(&cornerstone_keypair.to_bytes(), payload.as_bytes());

    // Take first 20 bits for 6-character code (1M+ combinations)
    let code_bits = u32::from_be_bytes([hmac[0], hmac[1], hmac[2], 0]) >> 12;
    let code = encode_base36(code_bits); // e.g., "AJ4R9X"

    (code, timestamp)
}

fn validate_invitation_code(
    code: &str,
    encrypted_request: &[u8],
    pebble_secret: &[u8],
    cornerstone_keypair: &Ed25519KeyPair,
    used_codes: &HashSet<String>
) -> Result<()> {
    // 1. Decrypt request with cornerstone private key
    let decrypted = lodesto (REVISED - Time-Based)**

```bash
# On cornerstone (or any pond Stone with cornerstone replica)
$ garden-rake invite stone

1. Calculate time-based invitation code:
   code = TOTP(pebble_secret + time_window)

2. Display code to user (screen only, NEVER on network):
   ┌─────────────────────────────────────┐
   │ Invitation Code: AJ4R9X             │
   │ Valid for: 5 minutes                │
   │ Expires at: 12:35:00 UTC            │
   │                                     │
   │ On new Stone, run:                  │
   │   garden-rake place stone AJ4R9X    │
   └─────────────────────────────────────┘

3. No network communication at this step
4. Cornerstone waits for encrypted join request

Notes:
- Code is deterministic (same time = same code)
- No pre-registration needed (validated on-demand)
- User must physically access cornerstone to see codee_bytes([hmac[0], hmac[1], hmac[2], 0]) >> 12;
        let expected_code = encode_base36(code_bits);

        if submitted_code == expected_code {
            return Ok(());
        }
    }

    Err(InvitationError::InvalidCode)
}
````

**Security Properties:**

✅ **Code never transmitted** - Displayed locally on cornerstone only  
✅ **Time-based** - Auto-expires after 5-minute window  
✅ **One-time use** - Tracked in used_codes set  
✅ **Encrypted request** - Cornerstone pubkey prevents network sniffing  
✅ **Clock drift tolerance** - ±5 minute window (±1 time slot)

⚠️ **Remaining Concerns:**

1. **Clock synchronization** - New Stone clock must be within ±5 min
2. **Cornerstone availability** - Single point of failure
3. **NTP dependency** - Requires network time sync
4. **Used codes storage** - Must persist across cornerstone restarts

#### Secure Handshake Protocol

**Phase 1: Pond Initialization**

```bash
$ garden-rake place pebble "My$ecureP@ssw0rd2026"

1. Validate passphrase strength (zxcvbn or similar)
2. Derive master key: Argon2id(passphrase, salt, params)
3. Generate Pond CA key pair (Ed25519)
4. Store CA cert in /etc/zen-garden/pond-ca.pem
5. Store encrypted master key in /etc/zen-garden/pond.enc
6. Broadcast "pond enabled" to all Stones via Moss API
7. Each Stone requests certificate from CA
8. Stones update mDNS announcements (add cert fingerprint)
```

**Phase 2: Stone Invitation**

````bash
# On existing pond Stone
$ garden-rake invite stone

1. Generate invitation code (6 chars, 5-min expiry)
2. Store pending invitation in Moss state:
   { (REVISED - Encrypted Request)**

```bash
# On new Stone (not yet in pond)
$ garden-rake place stone AJ4R9X

1. Discover cornerstone via mDNS:
   Query: _moss._tcp.local. (filter for cornerstone=true)
   Response: cornerstone public key + IP address

2. Check local clock synchronization:
   Query NTP server (pool.ntp.org)
   If drift > 5 minutes → warn user, attempt sync

3. Generate Ed25519 key pair for new Stone

4. Encrypt invitation code with cornerstone public key:
   encrypted_payload = encrypt_with_pubkey(cornerstone_pubkey, {
     "invitation_code": "AJ4R9X",
     "stone_name": "stone-04",
     "stone_public_key": "...",
     "timestamp": current_time
   })

5. Send encrypted join request to cornerstone:
   POST /api/pond/join
   Content-Type: application/octet-stream
   Body: encrypted_payload (binary, unreadable on network)

6. Cornerstone decrypts with private key and validates:
   a. Decrypt payload with cornerstone private key
   b. Calculate expected code: TOTP(pebble_secret + time_window, ±5 min)
   c. Check if submitted code matches expected
   d. Check if code already used (query used_codes table)
   e. Validate timestamp is within ±5 min window
   f. Validate stone_public_key format

7. If valid, cornerstone signs certificate:
   cert = sign_with_ca_key(new_stone_pubkey, expiration)

8. Cornerstone returns encrypted response:
   encrypted_response = encrypt_with_pubkey(new_stone_pubkey, {
     "pond_id": "...",
     "certificate": "...",
     "ca_certificate": "...",
     "cornerstone_address": "...",
     "pond_stones": ["stone-01", "stone-02", "stone-03"]
   })

9. New Stone decrypts response, stores pebble:
   /var/lib/zen-garden/pebble.enc

10. New Stone updates mDNS announcement:
    TXT: cert_fingerprint=sha256(certificate)

11. Cornerstone marks code as used:
    INSERT INTO used_codes (code, used_by, used_at)
    VALUES ('AJ4R9X', 'stone-04', NOW())

12. Cornerstone broadcasts "stone joined" to other pond Stones
````

**Security Advantages:**

✅ **Code never on network** - Only encrypted payload transmitted  
✅ **Replay protection** - Used codes tracked in database  
✅ **Time-bound** - ±5 min window, auto-expires  
✅ **No pre-registration** - Cornerstone validates on-demand  
✅ **Mutual encryption** - Both request and response encrypted}

5. New Stone stores pebble:
   /var/lib/zen-garden/pebble.enc

6. New Stone updates mDNS announcement:
   TXT: cert_fingerprint=sha256(certificate)

7. Pond Stone broadcasts "stone joined" to other Stones
8. Invitation code marked as used

```

**Phase 4: Mutual Authentication (Ongoing)**

```

Rake → Moss API (mTLS):

1. Rake loads pond CA cert
2. Rake connects to Moss with TLS
3. Moss presents certificate (signed by pond CA)
4. Rake validates cert against CA
5. If valid, proceed with API request
6. If invalid, refuse connection

Stone → Stone (service traffic):

- Same mTLS handshake
- Applications validate cert before connecting

````

#### Key Derivation Parameters

```toml
[crypto]
kdf = "argon2id"
argon2_memory = 65536  # 64 MB
argon2_iterations = 3
argon2_parallelism = 4
salt_length = 32       # bytes

[certificates]
algorithm = "ed25519"
validity_days = 365
````

#### Security Properties

✅ **Confidentiality:** mTLS encrypts all traffic  
✅ **Authentication:** Certificates prove Stone identity  
✅ **Integrity:** TLS prevents tampering  
✅ **Forward Secrecy:** Ephemeral session keys  
⚠️ **Non-Repudiation:** Not addressed (could add signing)  
❌ **Revocation:** Certificate revocation list (CRL) not specified

#### Recommendations (Priority)

**P0:**

1. Use **Argon2id** for passphrase → master key derivation
2. Use **Ed25519** for certificates (small, fast)
3. Store master key **encrypted at rest** (AES-256-GCM)
4. **6-character invitation codes** (20-bit entropy minimum)
5. **mTLS** for all Moss API endpoints (not optional)

**P1:**

1. Certificate **expiration** (1 year, auto-renew)
2. Certificate **revocation list** (CRL or OCSP)
3. **Hardware keystore support** (TPM, macOS Keychain, Windows DPAPI)
4. **Audit log signing** (prevent log tampering)

**P2:**

1. **Passphrase rotation** without re-issuing certs
2. **Stone attestation** (prove genuine Garden-Moss binary)
3. **Zero-knowledge proof** for invitation (no passphrase over wire)

---

### Identity & Access Management Specialist

**Overall Assessment:** 7.5/10 - Simple mental model, needs RBAC

#### Access Control Model

**Current Proposal:**

```
Open Mode:
- Anyone on network can manage Stones (install/remove services)
- Anyone can connect to services (no authentication)

Pond Mode:
- Only Rake with valid cert can manage Stones
- Only apps with valid cert can connect to services
```

**Gap: No role differentiation**

#### Proposed RBAC Model

**Roles:**

```yaml
admin:
  permissions:
    - pond.init
    - pond.disable
    - stone.invite
    - stone.revoke
    - service.install
    - service.uninstall
    - service.upgrade
    - config.update

operator:
  permissions:
    - service.install
    - service.uninstall
    - service.restart
    - service.view

readonly:
  permissions:
    - service.view
    - health.view
    - logs.view

service:
  permissions:
    - service.connect # Can connect to services, cannot manage
```

**Certificate Extensions:**

```
X.509 v3 Extensions:
  Subject: CN=stone-01
  Issuer: CN=Zen Garden Pond CA
  Extended Key Usage:
    - TLS Server Authentication
    - TLS Client Authentication
  Custom Extension (OID 1.3.6.1.4.1.99999.1):
    roles: ["admin"]
```

**Rake Profile (per user):**

```toml
# ~/.config/zen-garden/profile.toml
[user]
name = "alice"
role = "admin"

[certificates]
cert_path = "~/.config/zen-garden/alice.pem"
key_path = "~/.config/zen-garden/alice.key"
ca_path = "~/.config/zen-garden/ca.pem"
```

**Multi-User Pond:**

```bash
# Admin initializes pond
admin$ garden-rake place pebble "MyP@ssw0rd"

# Admin creates operator certificate
admin$ garden-rake issue cert bob --role operator
Certificate issued: bob.pem
Send to Bob: scp bob.pem bob@host:~/.config/zen-garden/

# Bob uses his certificate
bob$ garden-rake list
[uses bob.pem certificate, role=operator]

# Bob tries admin operation
bob$ garden-rake remove pebble
✗ Error: Permission denied (requires role: admin)
```

#### New Stone in Secure Pond (Limited Access)

**Current Proposal:**

> "When a new stone is added to a pond garden, it can be interacted with for management, but nothing else."

**Interpretation:**

```yaml
New Stone (before invitation):
  management_api: enabled # Can receive invite
  service_api: disabled # Cannot host services yet
  discovery: hidden # Not announced via mDNS

New Stone (after invitation):
  management_api: enabled
  service_api: enabled
  discovery: visible # Announced with cert fingerprint
```

**Implementation:**

```rust
// Moss API middleware
async fn require_invitation_or_cert(req: Request) -> Result<Request> {
    let endpoint = req.uri().path();

    match endpoint {
        "/api/pond/join" => {
            // No cert required (invitation code sufficient)
            Ok(req)
        }
        "/health" | "/info" => {
            // Read-only endpoints, no auth required
            Ok(req)
        }
        _ => {
            // All other endpoints require valid cert
            let cert = extract_client_cert(&req)?;
            validate_cert_against_pond_ca(cert)?;
            Ok(req)
        }
    }
}
```

#### Recommendations (Priority)

**P0:**

1. **Certificate-based authentication** for all Moss API endpoints
2. **Invitation-only management** for new Stones before pond join
3. **Audit log** (who did what, when)

**P1:**

1. **RBAC** with admin/operator/readonly roles
2. **Multi-user certificates** (one per person, not shared)
3. **Certificate issuance** (`garden-rake issue cert <name> --role <role>`)
4. **Certificate revocation** (`garden-rake revoke cert <name>`)

**P2:**

1. **Fine-grained permissions** (per-service ACLs)
2. **Temporary access** (time-limited certificates)
3. **Audit log queries** (`garden-rake audit --user bob --action service.install`)

---

### Operational Security Analyst

**Overall Assessment:** 6/10 - Happy path clear, failure modes undefined

#### Operational Concerns

**Concern 1: Passphrase Loss**

```
User forgets passphrase:
- Cannot disable pond
- Cannot revoke Stone
- Cannot issue new certificates

Recovery options?
- Master key backup (where? how?)
- Recovery codes (like 2FA backup codes)
- Multi-signature (2-of-3 admins can recover)
```

**Recommendation:** Recovery code system

```bash
$ garden-rake place pebble "MyP@ssw0rd"
✓ Pond initialized

Recovery codes (save these securely):
  1. XKCD-CORRECT-HORSE-BATTERY-STAPLE
  2. RAINBOW-TABLE-DEFENSE-MECHANISM
  3. ELLIPTIC-CURVE-CRYPTOGRAPHY-ROCKS

Any 1 code can disable pond if passphrase lost.
```

**Concern 2: Compromised Stone**

```
Stone is compromised (malware, physical access):
- Attacker extracts pebble (encrypted, but...)
- Attacker extracts logs (may contain sensitive data)
- Attacker uses Stone's cert to impersonate

Mitigation?
- Revoke Stone's certificate immediately
- Other Stones refuse connections from revoked cert
- Force Stone to re-join (new cert)
```

**Recommendation:** Revocation mechanism

```bash
$ garden-rake revoke stone stone-03
⚠ Warning: stone-03 will be removed from pond
Confirm (yes/no): yes
✓ Certificate revoked
✓ CRL updated
✓ All Stones notified

# On stone-03 (now revoked)
$ garden-rake status
✗ Error: Certificate revoked
To rejoin pond, run: garden-rake place stone <invitation-code>
```

**Concern 3: Pond Migration**

```
User wants to move Stones to new location:
- Physical move (same pond, different network)
- Logical move (join different pond)

Migration path?
- Export/import pond CA?
- Re-initialize pond on new network?
- Dual-home Stones (two ponds simultaneously)?
```

**Recommendation:** Pond export/import

```bash
# Export pond configuration
admin$ garden-rake export pond > pond-backup.json
{
  "pond_id": "...",
  "ca_certificate": "...",
  "stones": [...]
}

# Import on new admin machine
newadmin$ garden-rake import pond < pond-backup.json
Enter passphrase to decrypt: ****
✓ Pond imported
✓ 3 Stones recognized
```

**Concern 4: Certificate Renewal**

```
Certificates expire after 365 days:
- All Stones must renew before expiration
- What if admin is unavailable?
- What if Stone offline during renewal window?

Auto-renewal strategy?
- Stones request renewal 30 days before expiry
- Admin can approve/deny renewal requests
- Or: auto-approve renewals (less secure but operational)
```

**Recommendation:** Automatic renewal with notification

```bash
# 30 days before expiry
[moss] Certificate expires in 30 days, requesting renewal...
[moss] Renewal approved automatically
[moss] New certificate installed

# Admin notification
admin$ garden-rake audit --event cert.renewal
2026-01-15 12:00:00 stone-01 certificate renewed (expires 2027-01-15)
2026-01-15 12:05:00 stone-02 certificate renewed (expires 2027-01-15)
```

**Concern 5: Network Partition**

```
Network splits (firewall misconfiguration):
- Stones can't reach each other
- Apps can't discover Stones
- Certificate validation fails (CRL unreachable)

Graceful degradation?
- Cache last-known CRL (stale is better than unavailable)
- Allow connections if cert valid but CRL unreachable
- Log warnings for manual intervention
```

**Recommendation:** CRL caching + health checks

```rust
async fn validate_certificate(cert: &Certificate) -> Result<()> {
    // 1. Check signature against CA
    verify_signature(cert, pond_ca_cert())?;

    // 2. Check expiration
    if cert.expired() {
        return Err(CertError::Expired);
    }

    // 3. Check revocation
    match fetch_crl().await {
        Ok(crl) => {
            if crl.is_revoked(cert) {
                return Err(CertError::Revoked);
            }
        }
        Err(_) => {
            // CRL unreachable, check cache
            if let Some(cached_crl) = load_cached_crl() {
                if cached_crl.age() < Duration::from_hours(24) {
                    if cached_crl.is_revoked(cert) {
                        return Err(CertError::Revoked);
                    }
                } else {
                    log::warn!("CRL cache stale, allowing connection");
                }
            }
        }
    }

    Ok(())
}
```

#### Recommendations (Priority)

**P0:**

1. **Recovery codes** (5 codes, any 1 can disable pond)
2. **Certificate revocation** (CRL or OCSP)
3. **Automatic certificate renewal** (30 days before expiry)

**P1:**

1. **Pond export/import** (backup and restore)
2. **CRL caching** (graceful degradation on network partition)
3. **Health checks** (detect expired/revoked certs before failure)
4. **Audit log** (all security events)

**P2:**

1. **Multi-signature recovery** (2-of-3 admins)
2. **Hardware security module** (store master key in TPM)
3. **Certificate transparency log** (detect rogue certs)

---

### Developer Experience Analyst

**Overall Assessment:** 8.5/10 - Excellent UX, needs error guidance

#### User Journey: Enable Pond Security

**Happy Path:**

```bash
$ garden-rake place pebble "MySecureP@ssw0rd2026"

Validating passphrase...
✓ Passphrase strength: Good
  - Length: 20 characters
  - Mix: uppercase, lowercase, numbers, symbols
  - Not in common password list

Initializing pond...
  [1/5] Generating master key... ✓
  [2/5] Creating certificate authority... ✓
  [3/5] Discovering Stones... ✓ (found 3)
  [4/5] Issuing certificates... ✓
  [5/5] Updating announcements... ✓

✓ Pond enabled

Your recovery codes (save these):
  1. ALPHA-BRAVO-CHARLIE-DELTA
  2. ECHO-FOXTROT-GOLF-HOTEL
  3. INDIA-JULIET-KILO-LIMA

Security summary:
  - 3 Stones joined pond
  - All API requests now require certificates
  - All service connections use mTLS
  - mDNS announcements include cert fingerprints

Next steps:
  1. Test connection: garden-rake status
  2. Add new Stone: garden-rake invite stone
  3. View audit log: garden-rake audit
```

**Error Path: Weak Passphrase**

```bash
$ garden-rake place pebble "password"

✗ Error: Passphrase too weak

Issues:
  - Too short (8 characters, minimum 12 required)
  - Common password (found in breach database)
  - No numbers or symbols

Suggestions:
  - Use 12+ characters
  - Mix uppercase, lowercase, numbers, symbols
  - Use passphrase (4+ random words): "correct-horse-battery-staple"
  - Use password manager to generate strong passphrase

Try again: garden-rake place pebble "<strong-passphrase>"
```

**Error Path: Stones Unreachable**

```bash
$ garden-rake place pebble "MyP@ssw0rd"

Validating passphrase... ✓
Initializing pond... ✓
Discovering Stones... ⚠ (found 1 of 3)

⚠ Warning: Some Stones unreachable

Reachable:
  ✓ stone-01 (192.168.1.10)

Unreachable:
  ✗ stone-02 (last seen 2 hours ago)
  ✗ stone-03 (last seen 1 day ago)

Options:
  1. Continue with 1 Stone (other Stones can join later)
  2. Wait for Stones to come online (retry in 30 seconds)
  3. Cancel and investigate

Choice (1/2/3): 1

Proceeding with 1 Stone...
✓ Pond enabled (1 Stone joined)

To add unreachable Stones later:
  - Wait for them to come online
  - They will auto-request certificates
  - Approve with: garden-rake approve stone <name>
```

#### User Journey: Add New Stone

**Happy Path:**

```bash
# On existing Stone
$ garden-rake invite stone

Generating invitation code...
✓ Code generated

Invitation Code: AJ4R9X
Valid for: 5 minutes
Expires at: 12:35:00

On new Stone, run:
  garden-rake place stone AJ4R9X

Waiting for Stone to join...

# On new Stone
$ garden-rake place stone AJ4R9X

Connecting to pond...
  [1/4] Validating invitation code... ✓
  [2/4] Generating certificate request... ✓
  [3/4] Receiving certificate... ✓
  [4/4] Updating mDNS announcement... ✓

✓ Joined pond

Stone: stone-04
Pond: My Garden Pond
Certificate expires: 2027-01-15

# Back on inviting Stone
✓ Stone joined: stone-04
```

**Error Path: Code Expired**

```bash
$ garden-rake place stone AJ4R9X

Connecting to pond...
✗ Error: Invitation code expired

Code: AJ4R9X
Expired: 6 minutes ago

Request new invitation code from pond admin:
  garden-rake invite stone
```

**Error Path: Code Already Used**

```bash
$ garden-rake place stone AJ4R9X

Connecting to pond...
✗ Error: Invitation code already used

This code was used by: stone-03 (5 minutes ago)

Request new invitation code from pond admin:
  garden-rake invite stone
```

**Error Path: Too Many Attempts**

```bash
$ garden-rake place stone WRONG

Connecting to pond...
✗ Error: Invalid invitation code (9/10 attempts remaining)

$ garden-rake place stone WRONG2

✗ Error: Invalid invitation code (8/10 attempts remaining)

# After 10 failed attempts
$ garden-rake place stone WRONG10

✗ Error: Too many failed attempts

Code: WRONG10
Attempts: 10/10 (rate limit reached)

This invitation code is now locked for 30 minutes.
Request new invitation code from pond admin.
```

#### Recommendations

**P0:**

1. **Progress indicators** for multi-step operations
2. **Clear error messages** with suggested fixes
3. **Recovery code emphasis** (bold, warn about saving)
4. **Confirmation prompts** for destructive operations

**P1:**

1. **Interactive mode** (ask questions instead of flags)
2. **Dry-run mode** (`--dry-run` to preview changes)
3. **Undo command** (for recent operations)

---

## Team Consensus

### Strengths ✅

1. **Excellent UX** - Bluetooth-like pairing is intuitive
2. **Progressive security** - Start open, add security when needed
3. **No infrastructure** - No separate CA server, no public PKI
4. **Simple mental model** - "Place pebble" and "place stone" are clear actions
5. **Frictionless for home labs** - Pond is optional, not mandatory

### Critical Issues ❌

1. **4-character code insufficient** (16-bit entropy too low)
   - **Fix:** Use 6-character code (20-bit, ~1M combinations)
2. **No rate limiting specified** (brute force vulnerable)
   - **Fix:** Max 10 attempts per code, exponential backoff on passphrase

3. **No revocation mechanism** (compromised Stone can't be removed)
   - **Fix:** `garden-rake revoke stone` + CRL

4. **No recovery mechanism** (passphrase loss = permanent lockout)
   - **Fix:** Recovery codes (5 codes, any 1 can disable pond)

5. **No audit logging** (can't detect attacks or troubleshoot)
   - **Fix:** Structured audit log with security events

### Implementation Priorities

**Phase 1 (MVP Security):**

1. Argon2id passphrase → master key derivation
2. Ed25519 certificate generation
3. mTLS for Moss API (client cert authentication)
4. 6-character invitation codes (20-bit, 5-min expiry, one-time use)
5. Rate limiting (10 attempts per code, exponential backoff)
6. Recovery codes (5 codes, displayed once)

**Phase 2 (Operational Security):**

1. Certificate revocation (CRL, OCSP)
2. Audit logging (all security events)
3. Certificate renewal (auto-renew 30 days before expiry)
4. Pond export/import (backup/restore)
5. Health checks (detect expired/revoked certs)

**Phase 3 (Advanced Security):**

1. RBAC (admin/operator/readonly roles)
2. Multi-user certificates (one per person)
3. Hardware security module (TPM, Keychain, DPAPI)
4. Multi-signature recovery (2-of-3 admins)

---

## Revised Command Specification

### Enable Pond Security

```bash
garden-rake place pebble [--passphrase <passphrase>]

Options:
  --passphrase <passphrase>   Pond passphrase (prompted if not provided)
  --strength <weak|medium|strong>  Minimum passphrase strength (default: medium)
  --recovery-codes <count>    Number of recovery codes (default: 5)
  --cert-validity <days>      Certificate validity in days (default: 365)

Examples:
  garden-rake place pebble
  garden-rake place pebble --passphrase "MyP@ssw0rd2026"
  garden-rake place pebble --strength strong --recovery-codes 10

Security:
  - Passphrase minimum 12 characters
  - Must not be in common password list (10M+ breached passwords)
  - Recovery codes generated and displayed once
  - All Stones automatically issued certificates
  - All API requests require mTLS authentication
```

### Disable Pond Security

```bash
garden-rake remove pebble [--passphrase <passphrase>] [--recovery-code <code>]

Options:
  --passphrase <passphrase>   Pond passphrase (prompted if not provided)
  --recovery-code <code>      Recovery code (alternative to passphrase)
  --force                     Skip confirmation prompt

Examples:
  garden-rake remove pebble
  garden-rake remove pebble --passphrase "MyP@ssw0rd2026"
  garden-rake remove pebble --recovery-code "ALPHA-BRAVO-CHARLIE"

Security:
  - Requires passphrase OR any recovery code
  - Confirmation prompt (unless --force)
  - All certificates invalidated
  - All Stones revert to open mode
  - Audit log entry created
```

### Invite New Stone

```bash
garden-rake invite stone [--expires <minutes>] [--attempts <count>]

Options:
  --expires <minutes>   Code validity in minutes (default: 5)
  --attempts <count>    Max failed attempts before lockout (default: 10)
  --format <simple|qr>  Display format (default: simple)

Examples:
  garden-rake invite stone
  garden-rake invite stone --expires 10
  garden-rake invite stone --format qr  # Show QR code for mobile

Output:
  Invitation Code: AJ4R9X
  Valid for: 5 minutes
  Expires at: 12:35:00
  Max attempts: 10

Security:
  - Code is 6 characters (20-bit entropy, ~1M combinations)
  - One-time use (invalidated after successful join)
  - Expires after specified minutes
  - Rate limited (max attempts before lockout)
```

### Join Pond

```bash
garden-rake place stone <invitation-code>

Arguments:
  <invitation-code>   6-character invitation code

Examples:
  garden-rake place stone AJ4R9X

Security:
  - Validates code against pond CA
  - Generates Ed25519 key pair
  - Requests certificate from CA
  - Stores pebble (encrypted, /var/lib/zen-garden/pebble.enc)
  - Updates mDNS announcement (add cert fingerprint)
```

### Revoke Stone

```bash
garden-rake revoke stone <stone-name> [--reason <reason>]

Arguments:
  <stone-name>   Name of Stone to revoke

Options:
  --reason <reason>   Revocation reason (for audit log)

Examples:
  garden-rake revoke stone stone-03
  garden-rake revoke stone stone-03 --reason "Compromised"

Security:
  - Adds certificate to CRL
  - Notifies all Stones (update CRL)
  - Revoked Stone cannot connect until re-invited
  - Audit log entry created
```

---

## Open Questions for Product Team

1. **Default Security Posture:**
   - Ship with pond enabled or disabled by default?
   - Prompt user to enable pond on first `garden-rake` command?

2. **Recovery Code Storage:**
   - Display in terminal (user must save manually)?
   - Write to file (insecure but convenient)?
   - Both (display + write with warning)?

3. **Certificate Validity:**
   - 365 days reasonable? Too short? Too long?
   - Auto-renewal acceptable or require manual approval?

4. **Invitation Code Expiration:**
   - 5 minutes too short? Too long?
   - Configurable per invite or global setting?

5. **Rate Limiting:**
   - 10 attempts reasonable?
   - Exponential backoff or fixed lockout duration?

6. **Audit Log Retention:**
   - Store for how long? (30 days? 90 days? forever?)
   - Rotation strategy? (compress old logs?)

7. **Multi-User Support:**
   - Phase 1 or defer to Phase 2?
   - Single admin passphrase or multi-user from start?

---

## Final Recommendation

**Overall: 8/10 - Ship with refinements**

The proposed Pond security model is **excellent from a UX perspective** and **sound cryptographically** with the following changes:

### Must-Have Changes (P0):

1. ✅ **6-character invitation codes** (not 4)
2. ✅ **Rate limiting** (max 10 attempts, exponential backoff)
3. ✅ **Recovery codes** (5 codes, any 1 can disable pond)
4. ✅ **Certificate revocation** (CRL or OCSP)
5. ✅ **Audit logging** (all security events)
6. ✅ **mTLS for all Moss APIs** (not just service traffic)

### Should-Have Changes (P1):

1. ✅ Argon2id for passphrase derivation
2. ✅ Automatic certificate renewal
3. ✅ Pond export/import
4. ✅ Passphrase strength validation
5. ✅ Health checks for cert expiration

### Nice-to-Have (P2):

1. ⚪ RBAC (multi-user roles)
2. ⚪ Hardware security module support
3. ⚪ Multi-signature recovery

**Verdict:** Proceed with implementation after incorporating P0 changes.
