use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;
use std::time::SystemTime;

/// Tending state - persists indefinitely until explicitly changed or stone goes offline.
/// No TTL - Rake stays connected to the same stone across sessions.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TendingState {
    pub stone_name: String,
    pub endpoint: String,
    #[serde(with = "iso8601")]
    pub last_seen: SystemTime,
}

mod iso8601 {
    use serde::{Deserialize, Deserializer, Serialize, Serializer};
    use std::time::{SystemTime, UNIX_EPOCH};

    pub fn serialize<S>(time: &SystemTime, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let duration = time.duration_since(UNIX_EPOCH).map_err(serde::ser::Error::custom)?;
        let secs = duration.as_secs();
        let iso = chrono::DateTime::from_timestamp(secs as i64, 0)
            .ok_or_else(|| serde::ser::Error::custom("invalid timestamp"))?
            .to_rfc3339();
        iso.serialize(serializer)
    }

    pub fn deserialize<'de, D>(deserializer: D) -> Result<SystemTime, D::Error>
    where
        D: Deserializer<'de>,
    {
        let s = String::deserialize(deserializer)?;
        let dt = chrono::DateTime::parse_from_rfc3339(&s)
            .map_err(serde::de::Error::custom)?;
        let secs = dt.timestamp() as u64;
        Ok(UNIX_EPOCH + std::time::Duration::from_secs(secs))
    }
}

impl TendingState {
    /// Tending is always valid once set - no TTL expiration.
    /// Validity now depends on reachability (checked at use time in dispatch).
    pub fn is_valid(&self) -> bool {
        true
    }

    /// Age in seconds since tending was last written (informational only)
    pub fn age_seconds(&self) -> u64 {
        self.last_seen.elapsed().unwrap_or_default().as_secs()
    }
}

/// Get the zen-garden data directory, using platform-appropriate paths.
///
/// Priority order:
/// 1. Linux: XDG data directory (~/.local/share/zen-garden)
/// 2. All platforms: Home directory (~/.zen-garden)
/// 3. Linux fallback: /tmp/zen-garden (for containers/services)
fn zen_garden_dir() -> Result<PathBuf> {
    // On Linux, prefer XDG data directory
    #[cfg(target_os = "linux")]
    if let Some(data_dir) = dirs::data_local_dir() {
        let zen_dir = data_dir.join("zen-garden");
        if fs::create_dir_all(&zen_dir).is_ok() {
            return Ok(zen_dir);
        }
    }

    // Try home directory (works on all platforms)
    if let Some(home) = dirs::home_dir() {
        let zen_dir = home.join(".zen-garden");
        if fs::create_dir_all(&zen_dir).is_ok() {
            return Ok(zen_dir);
        }
    }

    // Linux fallback: /tmp for containers/services without home
    #[cfg(target_os = "linux")]
    {
        let tmp_dir = PathBuf::from("/tmp/zen-garden");
        fs::create_dir_all(&tmp_dir)
            .context("Failed to create /tmp/zen-garden directory")?;
        tracing::warn!("Using /tmp/zen-garden for tending state (no home/XDG available)");
        return Ok(tmp_dir);
    }

    // Non-Linux: error if no home directory
    #[cfg(not(target_os = "linux"))]
    anyhow::bail!("Could not determine home directory for tending state")
}

fn tending_file_path() -> Result<PathBuf> {
    Ok(zen_garden_dir()?.join(".tending"))
}

pub fn read_tending() -> Result<TendingState> {
    let path = tending_file_path()?;
    let content = fs::read_to_string(&path)
        .with_context(|| format!("Failed to read tending file: {}", path.display()))?;
    let state: TendingState = serde_json::from_str(&content)
        .context("Failed to parse tending file")?;
    Ok(state)
}

pub fn write_tending(stone_name: String, endpoint: String) -> Result<()> {
    let state = TendingState {
        stone_name,
        endpoint,
        last_seen: SystemTime::now(),
    };
    
    let path = tending_file_path()?;
    let content = serde_json::to_string_pretty(&state)
        .context("Failed to serialize tending state")?;
    fs::write(&path, content)
        .with_context(|| format!("Failed to write tending file: {}", path.display()))?;
    
    tracing::debug!(stone = %state.stone_name, endpoint = %state.endpoint, "Wrote tending state");
    Ok(())
}

pub fn clear_tending() -> Result<()> {
    let path = tending_file_path()?;
    if path.exists() {
        fs::remove_file(&path)
            .with_context(|| format!("Failed to remove tending file: {}", path.display()))?;
        tracing::debug!("Cleared tending state");
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn test_tending_state_always_valid() {
        // Tending is always valid - no TTL expiration
        let state = TendingState {
            stone_name: "test-stone".to_string(),
            endpoint: "http://127.0.0.1:7185".to_string(),
            last_seen: SystemTime::now(),
        };
        assert!(state.is_valid());
    }

    #[test]
    fn test_tending_state_valid_even_when_old() {
        // Even old tending state is valid - reachability is checked at use time
        let state = TendingState {
            stone_name: "test-stone".to_string(),
            endpoint: "http://127.0.0.1:7185".to_string(),
            last_seen: SystemTime::now() - Duration::from_secs(86400), // 24 hours old
        };
        assert!(state.is_valid());
        assert!(state.age_seconds() >= 86400);
    }
}
