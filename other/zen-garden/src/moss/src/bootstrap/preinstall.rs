//! Preinstall manifest loading
//!
//! Handles loading of pre-install manifest from disk for automated offering installation.
//! The manifest specifies offerings to install automatically at first boot.

/// Preinstall manifest structure
#[derive(Debug, serde::Deserialize)]
pub struct PreInstallManifest {
    pub offerings: Vec<String>,
    pub auto_install: bool,
}

/// Load preinstall manifest from disk
///
/// Looks for manifest at `/home/stone/garden-moss-preinstall.json`.
/// This is typically used for automated deployment scenarios where
/// offerings should be pre-installed at first boot.
///
/// # Returns
/// - `Some(manifest)`: Manifest found and parsed successfully
/// - `None`: No manifest found, or parse error
///
/// # Non-Blocking
/// This is an async function but should be called during startup,
/// not in hot paths.
///
/// # Example
/// ```rust,ignore
/// if let Some(manifest) = load_preinstall_manifest().await {
///     if manifest.auto_install {
///         // Spawn installation job for manifest.offerings
///     }
/// }
/// ```
pub async fn load_preinstall_manifest() -> Option<PreInstallManifest> {
    let path = "/home/stone/garden-moss-preinstall.json";
    if std::path::Path::new(path).exists() {
        tracing::info!("Found pre-install manifest at {}", path);
        match tokio::fs::read_to_string(path).await {
            Ok(content) => match serde_json::from_str(&content) {
                Ok(manifest) => {
                    tracing::info!("Loaded pre-install manifest with {} offerings",
                        serde_json::from_str::<serde_json::Value>(&content)
                            .ok()?
                            .get("offerings")?
                            .as_array()?
                            .len());
                    Some(manifest)
                },
                Err(e) => {
                    tracing::error!(error = ?e, "Failed to parse pre-install manifest");
                    None
                }
            },
            Err(e) => {
                tracing::error!(error = ?e, "Failed to read pre-install manifest");
                None
            }
        }
    } else {
        tracing::debug!("No pre-install manifest found at {}", path);
        None
    }
}
