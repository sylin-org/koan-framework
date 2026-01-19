# Pond Setup Guide

**Create and join Stones to a Pond for mTLS security**

**Purpose**: Step-by-step guide to enable Pond authentication  
**Audience**: Operator

---

## Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Create a Pond](#create-a-pond)
- [Join a Stone to Pond](#join-a-stone-to-pond)
- [Verify Pond Status](#verify-pond-status)
- [Troubleshooting](#troubleshooting)
- [Certificate Management](#certificate-management)

---

## Overview

**Pond** enables mTLS authentication between Stones. Start without Pond (frictionless), enable when ready.

**Philosophy**: "Set your stones, make sure everything is working, fill the pond."

**What Pond provides**:
- Authentication (verify Stone identity)
- Encryption (protect traffic from sniffing)
- Authorization (control which Stones can join)

---

## Prerequisites

### Before Creating Pond

✅ **All Stones operational** - Services running, discovery working  
✅ **Network connectivity** - All Stones can reach each other  
✅ **Time synchronization** - NTP configured (±10 min tolerance)  
✅ **Backups ready** - Important data backed up before security changes

### Strong Passphrase Required

Pond security depends on Keystone encryption. Use a strong passphrase:

**Zen Garden offers three generation methods:**
1. **Keyboard mashing** (default) - Fun & secure entropy collection
2. **Auto-generated** - XKCD-style 4-word passphrases
3. **Manual entry** - For password manager users

```bash
# Interactive generation (recommended)
garden-rake place keystone

How would you like to create your passphrase?
1. Let me mash the keyboard! 🎹 (fun & secure)
2. Generate one for me (quick & easy)
3. I'll type my own (advanced)

Choice [1]: 1

# Mash your keyboard for ~3 seconds
# System generates: forest-lantern-compass-71

# Or use standalone generator
garden-rake generate-passphrase

Output:
  Passphrase: autumn-laptop-database-71
  Entropy: 52 bits (strong)
  Memorization: "Autumn laptop database, room 71"
```

**Requirements:**
- Minimum: 40 bits entropy (validated automatically)
- Recommended: 52+ bits (4 XKCD words + number)
- Pattern: `word1-word2-word3-number` (e.g., `compass-twilight-harvest-82`)

**Learn more:** [Passphrase Generation UX](../proposals/passphrase-generation-ux.md), [XKCD 936](https://xkcd.com/936/)

Store passphrases securely (1Password, Bitwarden, etc.). **Never plain text.**

---

## Create a Pond

### Step 1: Choose Cornerstone

Select the first Stone to hold Pond authority (Cornerstone):

```bash
# On your chosen Stone (e.g., stone-01)
garden-rake place keystone

Initializing Pond...

How would you like to create your passphrase?
1. Let me mash the keyboard! (fun & secure)
2. Generate one for me (quick & easy)
3. I'll type my own (advanced)

Choice [1]: 1

Mash your keyboard randomly... GO!
████████████████████ 100%

✓ Collected 248 bits of entropy

Generated passphrase: forest-lantern-compass-71
Memorization: "A forest with lanterns and compass #71"

Use this passphrase? [Y/n]: y

✓ Pond created (hardware-backed via TPM 2.0)
✓ Cornerstone: stone-01
✓ Keystone sealed in TPM
✓ CA certificate generated

Next steps:
  1. Verify status: garden-rake status --security
  2. Join other Stones: garden-rake invite <stone-name>
```

**What happened**:
- Generated Pond CA keypair
- **Auto-detected TPM** and sealed Keystone in hardware (or encrypted with passphrase if no TPM)
- Issued certificate to stone-01 (1 hour TTL, auto-renews)
- Enabled mTLS mode (all future operations require certificates)

**Protection tier shown:** System automatically uses best available:
- `hardware-backed via TPM 2.0` - Sealed in physical security module
- `hypervisor-backed via KVM` - vTPM in virtual machine
- `software-backed` - Passphrase encryption (fallback)

**Learn more:** [Keystone Protection Tiers](../decisions/SECURITY-0003-keystone-protection-tiers.md)

### Step 2: Verify Cornerstone Status

```bash
garden-rake status --security

Pond Status: Active (Garden Pond - Tier 1)
Cornerstone: stone-01
Role: Cornerstone (CA authority)
Certificate: Valid, expires in 58 min
Auto-Renewal: Enabled (renews at 30 min)
Joined Stones: 1/10
Pending Joins: 0
Last Audit: 1 event (pond initialized)
```

---

## Join a Stone to Pond

### Step 3: Invite Stone from Cornerstone

On Cornerstone, generate invitation code:

```bash
# On stone-01 (Cornerstone)
garden-rake invite stone-02

Invitation ready for: stone-02

TOTP Code: KP7X9M
Valid for: 5 minutes
Expires at: 14:35:00 UTC

Display this code to the administrator adding stone-02.
Code will be shown on stone-02 screen during join attempt.
```

**Security model**: TOTP code displayed locally on both Stones (never transmitted over network). Inspired by Bluetooth pairing - familiar UX, proven security.

### Step 4: Join from New Stone

On the Stone you want to join:

```bash
# On stone-02
garden-rake join pond

Discovering Cornerstone...
✓ Found Cornerstone: stone-01

Requesting join...

TOTP Code on Cornerstone (stone-01):
Enter code: KP7X9M

Validating...
✓ Code valid
✓ Certificate issued
✓ Joined pond

Pond Status: Active (Garden Pond - Tier 1)
Cornerstone: stone-01
Role: Member
Certificate: Valid, expires in 60 min
Auto-Renewal: Enabled
```

**What happened**:
- stone-02 discovered stone-01 via mDNS
- Generated join request encrypted with Pond CA public key
- Administrator verified matching TOTP code (proves Cornerstone consent)
- Cornerstone issued certificate to stone-02
- stone-02 now authenticated in Pond

### Step 5: Repeat for All Stones

```bash
# Join stone-03
garden-rake invite stone-03   # On Cornerstone
garden-rake join pond          # On stone-03, enter code

# Join stone-04
garden-rake invite stone-04
garden-rake join pond
```

---

## Verify Pond Status

### Check All Stones

```bash
garden-rake status --security --all

stone-01 (Cornerstone):
  Certificate: Valid, expires in 42 min
  Role: Cornerstone
  Status: Healthy

stone-02:
  Certificate: Valid, expires in 35 min
  Role: Member
  Status: Healthy

stone-03:
  Certificate: Valid, expires in 28 min
  Role: Member
  Status: Healthy

stone-04:
  Certificate: Valid, expires in 51 min
  Role: Member
  Status: Healthy

Summary:
  Pond: Active
  Stones: 4 joined, 0 pending
  Certificates: All valid
  Security: mTLS enabled
```

### View Audit Log

```bash
garden-rake audit show --last 10

2026-01-18 14:30:15 | stone-01 | pond_initialized
2026-01-18 14:32:48 | stone-02 | stone_joined | source=stone-01
2026-01-18 14:35:12 | stone-03 | stone_joined | source=stone-01
2026-01-18 14:38:05 | stone-04 | stone_joined | source=stone-01
2026-01-18 14:45:30 | stone-01 | certificate_renewed
2026-01-18 14:48:22 | stone-02 | certificate_renewed
```

---

## Troubleshooting

### Join Failed: Invalid Code

```
Error: TOTP code validation failed

Possible causes:
  1. Code expired (5-minute TTL)
  2. Clock drift between Stones (±10 min tolerance)
  3. Typo in entered code
  4. Code already used (one-time use)

Solutions:
  1. Request new code: garden-rake invite <stone> (on Cornerstone)
  2. Check time sync: garden-rake check-time
  3. Verify NTP: systemctl status systemd-timesyncd
```

### Certificate Expiry Warning

```bash
garden-rake status --security

⚠️  Certificate expires in 5 minutes!
    Auto-renewal failed (Cornerstone unreachable)

Actions:
  1. Check network: ping stone-01
  2. Verify Cornerstone running: ssh stone-01 "systemctl status garden-moss"
  3. Manual renewal: garden-rake renew-certificate
```

**Auto-renewal**: Certificates renew every 30 minutes (50% of 1-hour lifetime). Manual renewal required only if auto-renewal fails.

### Keystone Passphrase Forgotten

```
Problem: Lost Keystone passphrase, cannot rotate CA or add new Stones

Recovery:
  1. No recovery possible (Keystone is encrypted, no backdoor)
  2. Options:
     a) Continue with existing Stones (certificates auto-renew)
     b) Create new Pond (requires re-joining all Stones)

Prevention:
  - Store passphrase in password manager
  - Backup Keystone file: /var/lib/zen-garden/keystone.enc
  - Test passphrase: garden-rake verify-keystone
```

### Stone Revoked (Security Incident)

```bash
# Revoke compromised Stone
garden-rake revoke stone-03 --reason "Suspected compromise"

✓ Stone revoked: stone-03
  Certificate expires in: 45 minutes (no renewal)
  Access blocked pond-wide immediately

# stone-03 loses Pond access within 1 hour (certificate TTL)
# All other Stones reject connections from stone-03
```

---

## Certificate Management

### Manual Renewal

```bash
# Renew certificate manually (normally automatic)
garden-rake renew-certificate

Requesting renewal from Cornerstone...
✓ Certificate renewed
  New expiry: 2026-01-18 15:45:00 UTC (60 minutes)
```

### Check Certificate Details

```bash
garden-rake certificate-info

Common Name: stone-02
Subject Alt Names:
  - stone-02.local
  - stone-02.pond
Issuer: Pond CA (stone-01)
Valid From: 2026-01-18 14:30:00 UTC
Valid Until: 2026-01-18 15:30:00 UTC
Time Remaining: 42 minutes
Auto-Renewal: Enabled (next renewal at 15:00:00)
```

### Rotate Pond CA

```bash
# Rotate Pond CA keypair (requires Keystone passphrase)
garden-rake rotate pond-ca

⚠️  Warning: This will invalidate all existing certificates.
    All Stones will receive new certificates automatically.

Enter Keystone passphrase:

Rotating Pond CA...
✓ New CA generated
✓ Keystone encrypted with new CA
✓ Broadcasting to all Stones...
✓ stone-02 renewed
✓ stone-03 renewed
✓ stone-04 renewed

Rotation complete. All Stones have new certificates.
```

**When to rotate**:
- Routine maintenance (every 90 days recommended)
- Security incident (Keystone compromised)
- Personnel change (admin left team)

---

## Operations Reference

### Common Commands

```bash
# Status
garden-rake status --security              # Local Stone security status
garden-rake status --security --all        # All Stones in Garden

# Join/Leave
garden-rake join pond                      # Join existing Pond
garden-rake leave pond                     # Leave Pond (graceful)

# Administration (Cornerstone only)
garden-rake invite <stone>                 # Generate TOTP for Stone join
garden-rake revoke <stone>                 # Revoke Stone access
garden-rake rotate pond-ca                 # Rotate CA keypair

# Certificate Management
garden-rake renew-certificate              # Manual renewal
garden-rake certificate-info               # View certificate details
garden-rake verify-keystone                # Test Keystone passphrase

# Audit
garden-rake audit show                     # View audit log
garden-rake audit --filter stone_joined    # Filter by event type

# Troubleshooting
garden-rake check-time                     # Verify time sync
garden-rake diagnose pond                  # Pond health check
```

---

## Related Documentation

- **[Security Overview](overview.md)** - Threat models, what Pond protects
- **[Threat Analysis](threat-analysis.md)** - Attack scenarios and mitigations
- **[Security Specification](../specs/security.md)** - Complete technical design
- **[TOTP Admission Proposal](../proposals/totp-admission.md)** - Stone admission workflow

---

**Last Updated**: 2026-01-18
