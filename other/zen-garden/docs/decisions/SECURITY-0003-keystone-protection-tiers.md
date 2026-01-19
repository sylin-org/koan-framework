# SECURITY-0003: Keystone Protection Tiers (TPM Auto-Detection)

**Status**: Proposed  
**Date**: 2026-01-18  
**Deciders**: Workshop panel (Security, DX, UX, Semiotics leads)

## Context

The Keystone (Pond CA keypair) is the foundational security artifact for Zen Garden. Protecting it from theft/extraction is critical. Different machines have different security capabilities:

- Modern PCs: TPM 2.0 hardware
- VMs: Virtual TPM (vTPM) via hypervisor
- Older/lightweight devices: Software encryption only

**Current implementation:** Software encryption with passphrase (AES-256-GCM)

**Problem:** Users don't know if they have TPM, and shouldn't need to decide. The system should automatically use the strongest available protection.

## Decision

**Implement automatic capability detection with three protection tiers:**

### Tier 1: Hardware TPM (Best)
- **Detection:** Check `/sys/class/tpm/tpm0` (Linux) or query via `tss-esapi`
- **Storage:** Seal keystone in TPM 2.0, protected by:
  - Passphrase (user authentication)
  - PCR values (boot integrity attestation)
- **Security:** Physical tamper resistance, keys never leave hardware
- **Label:** "hardware-backed"

### Tier 2: Virtual TPM (Good for VMs)
- **Detection:** Check hypervisor capabilities (libvirt, VirtualBox, VMware)
- **Storage:** Seal in vTPM provided by hypervisor
- **Security:** Depends on hypervisor isolation, better than software
- **Label:** "hypervisor-backed"

### Tier 3: Software Encryption (Fallback)
- **Detection:** No TPM available
- **Storage:** Encrypt with passphrase using AES-256-GCM
- **Security:** Strong encryption, but keys in memory during runtime
- **Label:** "software-backed"

**No user flags required.** System automatically selects best available tier.

## User Experience

### Placement (Auto-Detection)
```bash
$ garden-rake place keystone
Enter passphrase: ****

# On machine WITH hardware TPM:
✓ Keystone sealed in TPM 2.0 (hardware-backed)
  Keys stored in hardware security module
  
# On VM WITH vTPM:
✓ Keystone sealed in vTPM (hypervisor-backed)
  Protected by KVM hypervisor isolation
  
# On machine WITHOUT TPM:
✓ Keystone encrypted with passphrase (software-backed)
⚠ Consider enabling TPM in BIOS for stronger protection
  Learn more: https://zen-garden.dev/docs/security/tpm
```

### Status Check
```bash
$ garden-rake status

Stone: stone-02
Garden: my-garden
Security: Pond enabled (hardware-backed via TPM 2.0)

Recommendations:
  ✓ Using strongest available protection
  ✓ Keystone backup recommended (see docs/ops/maintainers.md)
```

### Status Check (Software Fallback)
```bash
$ garden-rake status

Stone: old-laptop
Garden: my-garden  
Security: Pond enabled (software-backed)

Recommendations:
  • Your system supports TPM 2.0 but it's disabled in BIOS
  • Enable TPM for hardware-backed security
  • Visit: https://zen-garden.dev/docs/security/enable-tpm
```

## Implementation

### Detection Logic
```rust
#[derive(Debug)]
pub enum KeystoneProtection {
    HardwareTPM { version: String },
    VirtualTPM { hypervisor: String },
    SoftwareEncrypted { algorithm: String },
}

impl KeystoneProtection {
    pub fn detect() -> Result<Self> {
        // Try hardware TPM first
        if let Some(tpm) = detect_hardware_tpm()? {
            return Ok(Self::HardwareTPM { 
                version: tpm.version() 
            });
        }
        
        // Try virtual TPM (VMs)
        if let Some(vtpm) = detect_virtual_tpm()? {
            let hypervisor = detect_virtualization()?
                .ok_or_else(|| anyhow!("vTPM without hypervisor"))?;
            return Ok(Self::VirtualTPM { 
                hypervisor: hypervisor.name() 
            });
        }
        
        // Fallback to software encryption
        Ok(Self::SoftwareEncrypted { 
            algorithm: "AES-256-GCM".into() 
        })
    }
    
    pub fn display_name(&self) -> &str {
        match self {
            Self::HardwareTPM { .. } => "hardware-backed",
            Self::VirtualTPM { .. } => "hypervisor-backed",
            Self::SoftwareEncrypted { .. } => "software-backed",
        }
    }
}
```

### TPM Detection (Linux)
```rust
fn detect_hardware_tpm() -> Result<Option<TpmInfo>> {
    use std::path::Path;
    
    // Check for TPM device
    if !Path::new("/dev/tpm0").exists() {
        return Ok(None);
    }
    
    // Query TPM via tss-esapi
    use tss_esapi::{Context, TctiNameConf};
    let mut context = Context::new(TctiNameConf::Device(Default::default()))?;
    
    let caps = context.get_capability(/* TPM version */)?;
    Ok(Some(TpmInfo {
        version: caps.version_string(),
        manufacturer: caps.manufacturer(),
    }))
}
```

### Keystone Sealing (TPM)
```rust
pub fn seal_keystone_in_tpm(
    keystone: &[u8],
    passphrase: &str,
    pcr_selection: &[u8],
) -> Result<SealedKeystone> {
    use tss_esapi::Context;
    
    let mut tpm = Context::new(/*...*/)?;
    
    // Create sealing object with:
    // - passphrase as auth value
    // - PCR policy (unseals only if boot state matches)
    let sealed = tpm.create_sealed_object(
        data: keystone,
        auth_value: passphrase,
        pcr_values: pcr_selection,
    )?;
    
    Ok(SealedKeystone {
        blob: sealed.to_bytes(),
        protection: KeystoneProtection::HardwareTPM { 
            version: "2.0".into() 
        },
    })
}
```

### Software Fallback
```rust
pub fn encrypt_keystone_software(
    keystone: &[u8],
    passphrase: &str,
) -> Result<EncryptedKeystone> {
    use aes_gcm::{Aes256Gcm, KeyInit};
    use argon2::Argon2;
    
    // Derive key from passphrase (Argon2id)
    let salt = generate_salt();
    let mut key = [0u8; 32];
    Argon2::default().hash_password_into(
        passphrase.as_bytes(),
        &salt,
        &mut key,
    )?;
    
    // Encrypt with AES-256-GCM
    let cipher = Aes256Gcm::new(&key.into());
    let nonce = generate_nonce();
    let ciphertext = cipher.encrypt(&nonce, keystone)?;
    
    Ok(EncryptedKeystone {
        ciphertext,
        salt,
        nonce,
        protection: KeystoneProtection::SoftwareEncrypted {
            algorithm: "AES-256-GCM".into(),
        },
    })
}
```

## Rationale

### Why Auto-Detection?
**Workshop consensus (Dr. Okonkwo, UX):** "Don't ask users to choose. There's no scenario where a user with TPM would want software encryption instead. The system should just do the right thing."

**Eliminated cognitive load:** Users don't need to know what TPM is. They just see "hardware-backed" (sounds good!) vs "software-backed" (okay, my machine doesn't have the hardware thing).

### Why Three Tiers?
**Security spectrum:** Hardware → Hypervisor → Software  
**Availability spectrum:** Rare (10% machines) → Common (VMs) → Universal

**Philosophy (Ravi, DX):** "Query capabilities, use the best option, inform the user what happened. No ceremony."

### Why Labels Matter
**Clear terminology (Marina, Semiotics):**
- ✅ "hardware-backed" → Physical security module
- ✅ "hypervisor-backed" → VM isolation
- ✅ "software-backed" → Encrypted files

**Confusing terminology (avoid):**
- ❌ "TPM-backed" → Ambiguous (hardware or virtual?)
- ❌ "Secure" vs "Insecure" → Value judgment, not descriptive

## Alternatives Considered

### Option A: User-Selected Flag (`--tpm`)
**Pros:** Explicit control  
**Cons:** Creates false choice, adds cognitive load

**Verdict:** Rejected. Auto-detection is superior UX.

### Option B: Software TPM Emulator (swtpm)
**Pros:** API compatibility for testing  
**Cons:** Zero security benefit over encrypted files (Prof. Chen, Semantics: "Software TPM is useful for testing but not a security upgrade")

**Verdict:** Support for dev/test only, don't advertise as security tier

### Option C: External HSM (YubiKey, CloudHSM)
**Pros:** Enterprise-grade security  
**Cons:** Requires hardware, complex setup

**Verdict:** Phase 3 feature for production deployments

## Consequences

**Positive:**
- Users get best available security automatically
- No installation flags to learn
- Clear messaging about protection level
- Future-proof (add HSM support without UX changes)

**Negative:**
- Requires TPM support crate (`tss-esapi`, ~200KB)
- Detection logic adds startup time (~50ms)
- BIOS-disabled TPM requires user action (documented)

**Neutral:**
- TPM adoption in consumer hardware still growing (~30% as of 2026)
- VM users benefit immediately (vTPM widely supported)

## Security Considerations

### Passphrase Strength
All tiers require strong passphrase. TPM protects *at rest* but not against weak passphrases. See SECURITY-0004 for passphrase generation requirements.

### PCR Selection (TPM)
Seal keystone to PCR 0, 2, 7 (UEFI firmware, boot loader, secure boot):
```rust
let pcr_selection = PcrSelection::new(&[0, 2, 7]);
```

**Rationale:** Balance security (unseals only if boot path unchanged) vs. usability (kernel updates don't break unsealing).

### Backup Recommendations
**All tiers require backup:**
```bash
sudo cp /var/lib/zen-garden/keystone.enc ~/backups/keystone-$(date +%Y%m%d).enc
```

TPM provides tamper resistance, not disaster recovery.

## Success Metrics

- **Adoption**: 80%+ of stones with TPM use hardware-backed protection
- **User awareness**: 90%+ of users can state their protection tier when asked
- **Support load**: <2% tickets about TPM setup issues

## References

- TPM 2.0 Specification: https://trustedcomputinggroup.org/tpm-2-0/
- `tss-esapi` crate: https://crates.io/crates/tss-esapi
- Workshop discussion (2026-01-18): Auto-detect capabilities, no user flags

## Related Decisions

- SECURITY-0002: Keystone rename (clarity on what we're protecting)
- SECURITY-0004: Passphrase generation UX (keyboard mashing, XKCD-style)

## Implementation Checklist

**Phase 1: Detection + Software Fallback**
- [ ] Implement `KeystoneProtection::detect()` with tier selection
- [ ] Update `place keystone` to show protection tier in output
- [ ] Add status command showing security posture
- [ ] Document TPM requirements and BIOS setup

**Phase 2: Hardware TPM Integration**
- [ ] Integrate `tss-esapi` for TPM 2.0 operations
- [ ] Implement sealing with PCR policy
- [ ] Test on real TPM hardware (multiple vendors)
- [ ] Handle TPM errors gracefully (fallback to software)

**Phase 3: Virtual TPM Support**
- [ ] Detect hypervisor (libvirt, VirtualBox, VMware, Hyper-V)
- [ ] Use vTPM when available in VMs
- [ ] Document vTPM setup per hypervisor
- [ ] Test across hypervisors

**Phase 4: Advanced Features**
- [ ] External HSM support (YubiKey, CloudHSM)
- [ ] Keystone rotation with TPM
- [ ] Multi-stone quorum (require 2-of-3 to unseal)

## Notes

**Key workshop insight (Dr. Tanaka, Security):** "The output message does the teaching, not the command interface. The presence of TPM becomes ambient information, not a decision point."

**Escape hatch:** Hidden `--force-software` flag for testing/debugging, but not documented prominently. Let 99% of users get the automatic behavior.
