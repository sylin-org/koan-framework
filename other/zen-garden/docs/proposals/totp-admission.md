# TOTP-Based Stone Admission

**Status:** Proposed  
**Priority:** Medium  
**Estimated Effort:** 2-3 days

## Problem

Current stone pool admission relies on manual configuration or network-based trust. Need stronger, more convenient authentication for adding stones to ponds.

## Proposed Solution

Use **Time-based One-Time Passwords (TOTP)** with Google Authenticator (or compatible OTP apps) for stone admission. Supports two modes:

1. **Stone-Level TOTP**: Each stone gets unique invitation with individual TOTP secret
2. **Pond-Level TOTP**: Entire pond shares one TOTP secret, any stone can join with the pond's code

### Mode 1: Stone-Level TOTP (Individual Invitations)

```bash
# Admin initiates stone invitation
$ garden-rake admin invite-stone stone-gamma

Invitation created for: stone-gamma

Scan this QR code with Google Authenticator:

████████████████████████████████████████████
██          ██    ████  ██          ██
██  ██████  ██  ██  ██  ██  ██████  ██
██  ██████  ██████  ██  ██  ██████  ██
██  ██████  ██    ██    ██  ██████  ██
██          ████████████████          ██
████████████  ██  ██  ██  ████████████
              ██  ██  ████
████  ██  ██  ████  ██████  ██  ████
  ██  ██  ████████  ██    ████████  ██
██  ████    ██  ██      ████  ████████
  ██  ██  ████    ████    ████  ██  ██
████████████      ████████████  ████
              ██████  ██  ██    ██  ██
██          ████  ██████  ████  ██████
██  ██████  ████  ████    ██████████
██  ██████  ██  ██████████████  ██  ██
██  ██████  ██  ██  ████████  ██  ████
██          ██      ██  ██████████
████████████████████████████████████████████

Or manually enter:
  Secret: JBSWY3DPEHPK3PXP
  Account: stone-gamma@zenarden
  Issuer: Zen Garden

# Stone connects and provides TOTP code
$ garden-moss --stone-name stone-gamma --join-pond alpha-pond
Enter TOTP code from authenticator: 123456
✓ Authenticated. Joining pond alpha-pond...
```

### Mode 2: Pond-Level TOTP (Network Discovery)

```bash
# Admin sets up pond with TOTP
$ garden-rake pond enable-totp

Pond TOTP enabled for: alpha-pond

Scan this QR code with Google Authenticator:

████████████████████████████████████████████
██          ██    ████  ██          ██
██  ██████  ██  ██  ██  ██  ██████  ██
██  ██████  ██████  ██  ██  ██████  ██
██  ██████  ██    ██    ██  ██████  ██
██          ████████████████          ██
████████████  ██  ██  ██  ████████████
              ██  ██  ████
████  ██  ██  ████  ██████  ██  ████
  ██  ██  ████████  ██    ████████  ██
██  ████    ██  ██      ████  ████████
  ██  ██  ████    ████    ████  ██  ██
████████████      ████████████  ████
              ██████  ██  ██    ██  ██
██          ████  ██████  ████  ██████
██  ██████  ████  ████    ██████████
██  ██████  ██  ██████████████  ██  ██
██  ██████  ██  ██  ████████  ██  ████
██          ██      ██  ██████████
████████████████████████████████████████████

Or manually enter:
  Secret: KBSWX4TFORHU2MBJ
  Account: alpha-pond@zengarden
  Issuer: Zen Garden

All stones must provide this code to join the pond.

# Later: Stone discovers pond on network
$ garden-moss --auto-discover

Discovered garden pond: alpha-pond
  Location: 192.168.1.50:7878
  Stones: 3 active
  Services: 12 offerings

// Stone-level invitation
pub async fn invite_stone(stone_name: &str) -> Result<()> {
    // Generate TOTP secret
    let secret = totp::generate_totp_secret();
    let uri = totp::generate_otpauth_uri(&secret,
        &format!("{}@zengarden", stone_name),
        "Zen Garden");

    // Display QR code
    println!("\nScan this QR code with Google Authenticator:\n");
    print_qr(&uri)?;

    println!("\nOr manually enter:");
    println!("  Secret: {}", secret.secret);
    println!("  Account: {}@zengarden", stone_name);
    println!("  Issuer: Zen Garden");

    // Store secret in pond config (encrypted)
    // Return invitation token
    Ok(())
}

// Pond-level TOTP setup
pub async fn enable_pond_totp(pond_name: &str) -> Result<()> {
    // Generate or retrieve pond TOTP secret
    let secret = totp::generate_totp_secret();
    let uri = totp::generate_otpauth_uri(&secret,
        &format!("{}@zengarden", pond_name),
        "Zen Garden");

    // Display QR code
    println!("\nScan this QR code with Google Authenticator:\n");
    print_qr(&uri)?;

    println!("\nOr manually enter:");
    println!("  Secret: {}", secret.secret);
    println!("  Account: {}@zengarden", pond_name);
    println!("  Issuer: Zen Garden");
    println!("\nAll stones must provide this code to join the pond.");

    // Store secret in pond config (encrypted)
    // Broadcast to existing stones
pub fn generate_totp_secret() -> TotpSecret {
    // Generate cryptographically secure random secret
    let mut rng = rand::thread_rng();
    let secret_bytes: [u8; 20] = rng.gen();
    let secret = base32::encode(base32::Alphabet::RFC4648 { padding: false }, &secret_bytes);

    TotpSecret {
        secret,
        algorithm: Algorithm::SHA1,
        digits: 6,
        period: 30,
    }
}

pub fn generate_otpauth_uri(secret: &TotpSecret, account: &str, issuer: &str) -> String {
    format!(
        "otpauth://totp/{}:{}?secret={}&issuer={}&algorithm={}&digits={}&period={}",
        issuer, account, secret.secret, issuer, secret.algorithm, secret.digits, secret.period
    )
}

pub fn verify_totp(secret: &str, code: &str) -> Result<bool> {
    // Verify code against current time window
    // Allow 1 window before/after for clock skew
}
```

**2. QR Code Display (garden-rake)**

````rust
// src/rake/src/commands/admin.rs
use qNetwork Discovery (garden-moss)**
```rust
// src/moss/src/discovery.rs
pub async fn discover_ponds() -> Result<Vec<PondInfo>> {
    // mDNS/broadcast discovery on local network
    // Return list of available ponds with metadata
    let ponds = mdns_discover_services("_zengarden._tcp.local").await?;
    Ok(ponds)
}

pub async fn join_pond_with_totp(pond_url: &str, totp_code: &str) -> Result<()> {
    // Connect to pond and request authentication
    let client = GardenHttpClient::new(pond_url);
    let response = client.post("/auth/totp", json!({
        "stone_name": get_local_stone_name()?,
        "totp_code": totp_code,
    })).await?;

    if !response.success {
        return Err(anyhow!("Authentication failed: {}", response.message));
    }

    // Store session token and pond config
 totp]
enabled = true
pond_secret_encrypted = "..."  # Pond-level TOTP secret (optional)
mode = "hybrid"  # "pond-only", "stone-only", or "hybrid"

[[invited_stones]]
name = "stone-gamma"
secret_encrypted = "..."  # Stone-level
````

**4. Verification (garden-moss)**

```rust
// src/moss/src/auth.rs
pub async fn authenticate_stone_join(stone_name: &str, totp_code: &str) -> Result<bool> {
    // Try pond-level TOTP first
    if let Some(pond_secret) = get_pond_totp_secret().await? {
        if totp::verify_totp(&pond_secret, totp_code)? {
            return Ok(true);
        }
    }

    // Fall back to stone-level invitation
    if let Some(stone_secret) = retrieve_stone_secret(stone_name).await? {
        if totp::verify_totp(&stone_secret, totp_code)? {
            return Ok(true);
        }
    }

    Err(anyhow!("Invalid TOTP code")!("  Secret: {}", secret.secret);
    println!("  Account: {}@zengarden", stone_name);
    println!("  Issuer: Zen Garden");

    // Store secret in pond config (encrypted)
    // Return invitation token
    Ok(())
}
```

**3. Verification (garden-moss)**

````rust
// src/moss/src/auth.rs
pub async fn authenticate_stone_join(stone_name: &str, totp_code: &str) -> Result<bool> {
    // Retrieve stored secret for stone from pond config
    let secret = retrieve_stone_secret(stone_name).await?;

    // Verify TOTP code
    if !totp::verify_totp(&secret, totp_code)? {
        return Err(anyhow!("Invalid TOTP code"));
    }

    // Mark stone as authenticated
    // Issue session token
### General
1. **Secret Storage**: Encrypt TOTP secrets at rest using system keyring or stone-specific encryption key
2. **Clock Skew**: Accept codes from ±1 time window (±30s) to handle minor clock drift
3. **Replay Protection**: Track used codes to prevent replay attacks within same window
4. **Rate Limiting**: Limit TOTP verification attempts (3 failures = temporary lockout, 10 = permanent block)

### Stone-Level Mode
5. **Expiry**: Invitation secrets expire after 7 days if unused
6. **Revocation**: Admin can revoke invitations before stone joins
7. **One-Time Use**: Stone invitation secret consumed after successful join

### Pond-Level Mode
8. **Secret Rotation**: Admin can rotate pond TOTP secret (requires re-scanning for all stones)
9. **Broadcast Security**: Pond TOTP secret distributed to existing stones via encrypted channel
10. **Discovery Filtering**: Ponds can hide from network discovery, requiring manual endpoint entry
11. **Shared Secret Risk**: All stones with pond access can authenticate new stones - trust model consideration
**Pond Configuration**
```toml
# /etc/zengarden/pond.toml
[[invited_stones]]
name = "stone-gamma"
secret_encrypted = "..."  # Encrypted TOTP secret
invited_at = "2026-01-18T13:05:00Z"
expires_at = "2026-01-25T13:05:00Z"  # 7 day expiry
````

**Stone Configuration (after join)**

```toml
# /etc/zengarden/moss.toml
[pond]
name = "alpha-pond"
totp_secret = "..."  # For ongoing authentication
```

## Dependencies

```toml
# src/common/Cargo.toml
[dependencies]
totpode Selection Guidance

**Use Stone-Level TOTP when:**
- Individual stone accountability required
- Different trust levels for different stones
- Temporary/guest stone access needed
- Audit trail of which stones were invited when

**Use Pond-Level TOTP when:**
- Quick onboarding of many stones
- All stones equally trusted
- Simplified administration (one secret for whole pond)
- Dynamic environments (stones frequently join/leave)

**Use Hybrid Mode when:**
- Default to pond-level for convenience
- Fall back to stone-level for sensitive/special cases
- Maximum flexibility for different scenarios

## Migration Path

**Phase 1: Optional TOTP** (v0.2)
- Add both TOTP modes alongside existing admission methods
- Opt-in via `--require-totp` flag on pond creation
- Choose mode: `--totp-mode=[pond|stone|hybrid]`
- Graceful degradation if TOTP unavailable

**Phase 2: Default TOTP** (v0.3)
- TOTP becomes default for new ponds (hybrid mode)
- Old ponds can enable via `garden-rake pond enable-totp`
- Legacy admission still supported with warning

**Phase 3: Mandatory TOTP** (v1.0)
- Remove legacy admission methods
- All stones require TOTP for production use
- Default to hybrid mode, admin chooses restriction level
## Migration Path

**Phase 1: Optional TOTP** (v0.2)
- Add TOTP support alongside existing admission methods
- Opt-in via `--require-totp` flag on pond creation
- Graceful degradation if TOTP unavailable

**Phase 2: Default TOTP** (v0.3)
- TOTP becomes default for new ponds
- Old ponds can enable via `garden-rake pond enable-totp`
5. **Pond Secret Sharing**: How do existing stones receive pond TOTP secret securely when enabled post-creation?
6. **Discovery Privacy**: Should ponds broadcast TOTP-required status in discovery response?
7. **Grace Period**: When rotating pond secret, how long do we accept both old and new codes?
8. **Stone Trust**: In pond-level mode, can any authenticated stone add offerings, or separate authorization layer?
- Legacy admission still supported with warning

**Phase 3: Mandatory TOTP** (v1.0)
- Remove legacy admission methods
- All stones require TOTP for production use

## Alternative Approaches Considered

**1. Shar Core TOTP Infrastructure**
- Day 1-2: Implement TOTP generation/verification in common
- Day 3: Add QR code display to rake admin commands
- Day 4: Integrate verification into moss stone admission

**Week 2: Pond-Level Mode**
- Day 5: Network discovery for ponds (mDNS/broadcast)
- Day 6: Pond-level TOTP setup and validation
- Day 7: Encrypted secret storage for both modes

**Week 3: Polish & Security**
- Day 8: Rate limiting and replay protection
- Day 9: Secret rotation mechanisms
- Day 10: Testing, documentation, examples

**3. Cloud-Based Auth Provider**
- ❌ External dependency
- ❌ Network requirement
- ❌ Goes against self-hosted philosophy

**✓ TOTP Advantages:**
- Standard protocol (RFC 6238)
- Works offline (no network required)
- Familiar to users (Google Authenticator)
- Easy rotation (new invitation = new secret)
- Low complexity

## Open Questions

1. **Backup Codes**: Should we generate one-time backup codes for emergency access?
2. **Multi-Admin**: How do multiple admins manage stone invitations? Shared TOTP or separate?
3. **Stone-to-Stone**: Do stones need TOTP for discovering each other within a pond?
4. **UI**: Should rake offer option to display as image file instead of terminal QR?

## Success Criteria

- [ ] Admin can generate invitation with QR code in terminal
- [ ] Stone can join pond using TOTP code from authenticator app
- [ ] Invalid codes are rejected with clear error message
- [ ] Secrets are encrypted at rest
- [ ] Documentation includes setup guide with screenshots
- [ ] Works with Google Authenticator, Authy, 1Password, Bitwarden

## Timeline

**Week 1:**
- Day 1-2: Implement TOTP generation/verification in common
- Day 3: Add QR code display to rake admin commands
- Day 4: Integrate verification into moss stone admission

**Week 2:**
- Day 5: Encrypted secret storage
- Day 6: Rate limiting and replay protection
- Day 7: Testing, documentation, polish

## Related

- **Security Model**: docs/architecture/security.md
- **Pond Clustering**: docs/decisions/POND-*.md
- **Stone Lifecycle**: docs/architecture/stone-lifecycle.md
```
