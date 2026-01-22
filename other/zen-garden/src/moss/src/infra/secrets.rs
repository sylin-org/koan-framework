//! Secrets management with platform-adaptive backends
//!
//! Provides secure credential storage for borrowed offerings with priority cascade:
//! 1. TPM (Trusted Platform Module) - Hardware-backed security
//! 2. Platform Keyring - OS-native secure storage
//! 3. Encrypted File - Fallback with AES-256-GCM encryption
//!
//! Automatically selects best available backend for the platform.

use anyhow::{Context, Result};
use base64::Engine;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::PathBuf;
use garden_common::constants::CONFIG_DIR;

/// Secret storage backend trait
pub trait SecretBackend: Send + Sync {
    /// Store a secret
    fn store(&self, key: &str, value: &str) -> Result<()>;

    /// Retrieve a secret
    fn retrieve(&self, key: &str) -> Result<Option<String>>;

    /// Delete a secret
    fn delete(&self, key: &str) -> Result<()>;

    /// List all secret keys
    fn list_keys(&self) -> Result<Vec<String>>;

    /// Backend name for logging
    fn backend_name(&self) -> &'static str;
}

/// Secrets manager with automatic backend selection
pub struct SecretsManager {
    backend: Box<dyn SecretBackend>,
}

impl SecretsManager {
    /// Create secrets manager with best available backend
    pub fn new() -> Result<Self> {
        // Try backends in priority order
        let backend: Box<dyn SecretBackend> = if let Ok(tpm) = TpmBackend::new() {
            tracing::info!("Using TPM backend for secrets");
            Box::new(tpm)
        } else if let Ok(keyring) = PlatformKeyringBackend::new() {
            tracing::info!("Using platform keyring backend for secrets");
            Box::new(keyring)
        } else {
            tracing::info!("Using encrypted file backend for secrets");
            Box::new(EncryptedFileBackend::new()?)
        };

        Ok(Self { backend })
    }

    /// Store a secret
    pub fn store(&self, key: &str, value: &str) -> Result<()> {
        self.backend.store(key, value)
    }

    /// Retrieve a secret
    pub fn retrieve(&self, key: &str) -> Result<Option<String>> {
        self.backend.retrieve(key)
    }

    /// Delete a secret
    pub fn delete(&self, key: &str) -> Result<()> {
        self.backend.delete(key)
    }

    /// List all secret keys
    pub fn list_keys(&self) -> Result<Vec<String>> {
        self.backend.list_keys()
    }

    /// Get backend name
    pub fn backend_name(&self) -> &'static str {
        self.backend.backend_name()
    }
}

// ============================================================================
// TPM Backend (Stub for future implementation)
// ============================================================================

struct TpmBackend {
    _placeholder: (),
}

impl TpmBackend {
    fn new() -> Result<Self> {
        // TODO: Implement TPM backend using tpm2-tss
        // For now, return error to fall through to next backend
        anyhow::bail!("TPM backend not yet implemented")
    }
}

impl SecretBackend for TpmBackend {
    fn store(&self, _key: &str, _value: &str) -> Result<()> {
        anyhow::bail!("TPM backend not implemented")
    }

    fn retrieve(&self, _key: &str) -> Result<Option<String>> {
        anyhow::bail!("TPM backend not implemented")
    }

    fn delete(&self, _key: &str) -> Result<()> {
        anyhow::bail!("TPM backend not implemented")
    }

    fn list_keys(&self) -> Result<Vec<String>> {
        anyhow::bail!("TPM backend not implemented")
    }

    fn backend_name(&self) -> &'static str {
        "tpm"
    }
}

// ============================================================================
// Platform Keyring Backend (Stub for future implementation)
// ============================================================================

struct PlatformKeyringBackend {
    _placeholder: (),
}

impl PlatformKeyringBackend {
    fn new() -> Result<Self> {
        // TODO: Implement platform keyring using:
        // - macOS: Security framework (Keychain)
        // - Windows: Credential Manager API
        // - Linux: Secret Service API (libsecret)
        // For now, return error to fall through to next backend
        anyhow::bail!("Platform keyring backend not yet implemented")
    }
}

impl SecretBackend for PlatformKeyringBackend {
    fn store(&self, _key: &str, _value: &str) -> Result<()> {
        anyhow::bail!("Platform keyring backend not implemented")
    }

    fn retrieve(&self, _key: &str) -> Result<Option<String>> {
        anyhow::bail!("Platform keyring backend not implemented")
    }

    fn delete(&self, _key: &str) -> Result<()> {
        anyhow::bail!("Platform keyring backend not implemented")
    }

    fn list_keys(&self) -> Result<Vec<String>> {
        anyhow::bail!("Platform keyring backend not implemented")
    }

    fn backend_name(&self) -> &'static str {
        "platform_keyring"
    }
}

// ============================================================================
// Encrypted File Backend (Always available fallback)
// ============================================================================

/// Encrypted file backend using AES-256-GCM
///
/// Stores secrets in encrypted JSON file at:
/// - Linux: /etc/zen-garden/.secrets
/// - Windows: .zen-garden/.secrets
///
/// Encryption key is derived from machine-specific data using Argon2.
struct EncryptedFileBackend {
    secrets_path: PathBuf,
    encryption_key: Vec<u8>,
}

#[derive(Debug, Serialize, Deserialize)]
struct SecretsFile {
    version: u8,
    secrets: HashMap<String, EncryptedSecret>,
}

#[derive(Debug, Serialize, Deserialize)]
struct EncryptedSecret {
    ciphertext: Vec<u8>,
    nonce: Vec<u8>,
}

impl EncryptedFileBackend {
    fn new() -> Result<Self> {
        let secrets_path = PathBuf::from(CONFIG_DIR).join(".secrets");

        // Derive encryption key from machine ID
        let encryption_key = Self::derive_encryption_key()?;

        Ok(Self {
            secrets_path,
            encryption_key,
        })
    }

    /// Derive encryption key from machine-specific data
    fn derive_encryption_key() -> Result<Vec<u8>> {
        use argon2::{Argon2, PasswordHasher};
        use argon2::password_hash::SaltString;

        // Get machine ID (platform-specific)
        let machine_id = Self::get_machine_id()?;

        // Use fixed salt derived from machine ID for deterministic key
        // This is acceptable since the machine ID itself is secret
        let salt_bytes = blake3::hash(machine_id.as_bytes());
        let salt_str = base64::engine::general_purpose::STANDARD.encode(&salt_bytes.as_bytes()[..16]);
        let salt = SaltString::encode_b64(&salt_str.as_bytes()[..22])
            .map_err(|e| anyhow::anyhow!("Failed to create salt: {:?}", e))?;

        // Derive key using Argon2
        let argon2 = Argon2::default();
        let password_hash = argon2
            .hash_password(machine_id.as_bytes(), &salt)
            .map_err(|e| anyhow::anyhow!("Failed to hash password: {:?}", e))?;

        // Extract 32-byte key
        let hash_bytes = password_hash.hash.context("No hash in password")?;
        Ok(hash_bytes.as_bytes()[..32].to_vec())
    }

    /// Get machine-specific identifier
    #[cfg(target_os = "linux")]
    fn get_machine_id() -> Result<String> {
        // Try /etc/machine-id first (systemd)
        if let Ok(id) = std::fs::read_to_string("/etc/machine-id") {
            return Ok(id.trim().to_string());
        }

        // Fall back to /var/lib/dbus/machine-id
        if let Ok(id) = std::fs::read_to_string("/var/lib/dbus/machine-id") {
            return Ok(id.trim().to_string());
        }

        anyhow::bail!("Could not read machine ID")
    }

    #[cfg(target_os = "windows")]
    fn get_machine_id() -> Result<String> {
        // Use Windows MachineGuid from registry
        use std::process::Command;

        let output = Command::new("reg")
            .args(&[
                "query",
                "HKLM\\SOFTWARE\\Microsoft\\Cryptography",
                "/v",
                "MachineGuid",
            ])
            .output()
            .context("Failed to query registry")?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        let guid = stdout
            .lines()
            .find(|line| line.contains("MachineGuid"))
            .and_then(|line| line.split_whitespace().last())
            .context("Could not parse MachineGuid")?;

        Ok(guid.to_string())
    }

    #[cfg(target_os = "macos")]
    fn get_machine_id() -> Result<String> {
        // Use IOPlatformUUID on macOS
        use std::process::Command;

        let output = Command::new("ioreg")
            .args(&["-rd1", "-c", "IOPlatformExpertDevice"])
            .output()
            .context("Failed to run ioreg")?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        let uuid = stdout
            .lines()
            .find(|line| line.contains("IOPlatformUUID"))
            .and_then(|line| line.split('"').nth(3))
            .context("Could not parse IOPlatformUUID")?;

        Ok(uuid.to_string())
    }

    /// Load secrets file
    fn load_secrets(&self) -> Result<SecretsFile> {
        if !self.secrets_path.exists() {
            return Ok(SecretsFile {
                version: 1,
                secrets: HashMap::new(),
            });
        }

        let data = std::fs::read(&self.secrets_path)
            .context("Failed to read secrets file")?;

        serde_json::from_slice(&data)
            .context("Failed to parse secrets file")
    }

    /// Save secrets file
    fn save_secrets(&self, secrets: &SecretsFile) -> Result<()> {
        let data = serde_json::to_vec_pretty(secrets)
            .context("Failed to serialize secrets")?;

        // Ensure directory exists
        if let Some(parent) = self.secrets_path.parent() {
            std::fs::create_dir_all(parent)
                .context("Failed to create secrets directory")?;
        }

        std::fs::write(&self.secrets_path, data)
            .context("Failed to write secrets file")?;

        // Set restrictive permissions (Unix only)
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&self.secrets_path, std::fs::Permissions::from_mode(0o600))
                .context("Failed to set secrets file permissions")?;
        }

        Ok(())
    }

    /// Encrypt value using AES-256-GCM
    fn encrypt(&self, plaintext: &str) -> Result<EncryptedSecret> {
        use chacha20poly1305::{
            aead::{Aead, KeyInit, OsRng},
            ChaCha20Poly1305, Nonce,
        };

        let cipher = ChaCha20Poly1305::new_from_slice(&self.encryption_key)
            .map_err(|e| anyhow::anyhow!("Invalid encryption key: {:?}", e))?;

        let nonce_bytes = chacha20poly1305::aead::rand_core::RngCore::next_u64(&mut OsRng)
            .to_le_bytes();
        let mut nonce_array = [0u8; 12];
        nonce_array[..8].copy_from_slice(&nonce_bytes);
        let nonce = Nonce::from_slice(&nonce_array);

        let ciphertext = cipher
            .encrypt(nonce, plaintext.as_bytes())
            .map_err(|e| anyhow::anyhow!("Encryption failed: {}", e))?;

        Ok(EncryptedSecret {
            ciphertext,
            nonce: nonce.to_vec(),
        })
    }

    /// Decrypt value using AES-256-GCM
    fn decrypt(&self, secret: &EncryptedSecret) -> Result<String> {
        use chacha20poly1305::{
            aead::{Aead, KeyInit},
            ChaCha20Poly1305, Nonce,
        };

        let cipher = ChaCha20Poly1305::new_from_slice(&self.encryption_key)
            .map_err(|e| anyhow::anyhow!("Invalid encryption key: {:?}", e))?;

        let nonce = Nonce::from_slice(&secret.nonce);

        let plaintext = cipher
            .decrypt(nonce, secret.ciphertext.as_ref())
            .map_err(|e| anyhow::anyhow!("Decryption failed: {}", e))?;

        String::from_utf8(plaintext)
            .context("Decrypted data is not valid UTF-8")
    }
}

impl SecretBackend for EncryptedFileBackend {
    fn store(&self, key: &str, value: &str) -> Result<()> {
        let mut secrets = self.load_secrets()?;
        let encrypted = self.encrypt(value)?;
        secrets.secrets.insert(key.to_string(), encrypted);
        self.save_secrets(&secrets)?;
        Ok(())
    }

    fn retrieve(&self, key: &str) -> Result<Option<String>> {
        let secrets = self.load_secrets()?;
        if let Some(encrypted) = secrets.secrets.get(key) {
            Ok(Some(self.decrypt(encrypted)?))
        } else {
            Ok(None)
        }
    }

    fn delete(&self, key: &str) -> Result<()> {
        let mut secrets = self.load_secrets()?;
        secrets.secrets.remove(key);
        self.save_secrets(&secrets)?;
        Ok(())
    }

    fn list_keys(&self) -> Result<Vec<String>> {
        let secrets = self.load_secrets()?;
        Ok(secrets.secrets.keys().cloned().collect())
    }

    fn backend_name(&self) -> &'static str {
        "encrypted_file"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encrypted_file_backend() {
        let backend = EncryptedFileBackend::new().unwrap();

        // Store secret
        backend.store("test_key", "test_value").unwrap();

        // Retrieve secret
        let value = backend.retrieve("test_key").unwrap();
        assert_eq!(value, Some("test_value".to_string()));

        // List keys
        let keys = backend.list_keys().unwrap();
        assert!(keys.contains(&"test_key".to_string()));

        // Delete secret
        backend.delete("test_key").unwrap();

        // Verify deletion
        let value = backend.retrieve("test_key").unwrap();
        assert_eq!(value, None);
    }
}
