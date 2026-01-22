//! Offerings index management
//!
//! Business logic for:
//! - Building offerings index from templates
//! - Caching compiled offerings with fingerprinting
//! - Template hashing for cache invalidation
//!
//! Composed with compatibility module for rule evaluation.

use anyhow::Result;
use crate::templates::TemplateLoader;
use crate::domain::compatibility::{CompiledCompatibility, compile_compatibility};

/// Compiled offering ready for API consumption
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct CompiledOffering {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
    pub image: String, // effective image after compatibility evaluation
    pub ports: Vec<(u16, u16)>,
    pub environment: Vec<String>,
    pub volumes: Vec<(String, String)>,
    pub compatibility: CompiledCompatibility,
}

/// Fingerprint for cache invalidation
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize, PartialEq, Eq)]
pub struct OfferingsFingerprint {
    pub moss_version: String,
    pub capabilities_hash: String,
    pub templates_hash: String,
}

/// Cached offerings index with fingerprint
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct OfferingsIndexCache {
    pub fingerprint: OfferingsFingerprint,
    pub generated_at: String,
    pub offerings: Vec<CompiledOffering>,
}

/// Get moss version string (from Cargo.toml + build number)
pub fn moss_version_string() -> String {
    // build.rs injects BUILD_NUMBER (see src/moss/src/discovery.rs)
    format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER"))
}

/// Hash arbitrary bytes with BLAKE3
fn blake3_hex(bytes: &[u8]) -> String {
    blake3::hash(bytes).to_hex().to_string()
}

/// Generate capabilities hash for fingerprinting
///
/// Includes CPU, memory, GPU/AI capabilities.
/// Changes trigger offerings index rebuild.
pub fn current_capabilities_hash() -> String {
    let caps = crate::domain::compatibility::get_current_compat_capabilities();
    let gpus = crate::metrics::detect_gpus();

    // Include GPU/AI capabilities in hash so offerings re-evaluate when AI hardware is detected
    // Helper to check if any GPU has a runtime (supports both "cuda" and "cuda:12.2" formats)
    let has_runtime = |runtime_name: &str| {
        gpus.iter().any(|g| {
            g.ai_runtimes.iter().any(|r| {
                let r_lower = r.to_lowercase();
                let runtime_lower = runtime_name.to_lowercase();
                // Match either exact or base runtime (e.g., "cuda" matches "cuda:12.2")
                r_lower == runtime_lower || r_lower.starts_with(&format!("{}:", runtime_lower))
            })
        })
    };

    let has_cuda = has_runtime("cuda");
    let has_rocm = has_runtime("rocm");
    let has_directml = has_runtime("directml");
    let has_openvino = has_runtime("openvino");
    let gpu_vram_total: u64 = gpus.iter().filter_map(|g| g.vram_mb).sum();

    let payload = serde_json::json!({
        "cpu_model": caps.cpu_model,
        "cpu_features": caps.cpu_features,
        "architecture": caps.architecture,
        "total_memory_mb": caps.total_memory_mb,
        "has_cuda": has_cuda,
        "has_rocm": has_rocm,
        "has_directml": has_directml,
        "has_openvino": has_openvino,
        "gpu_vram_total_mb": gpu_vram_total,
    });
    blake3_hex(serde_json::to_vec(&payload).unwrap_or_default().as_slice())
}

/// Compute hash of all templates for cache invalidation
///
/// Includes moss version, template names, and all configuration.
/// Changes trigger offerings index rebuild.
pub async fn templates_hash(templates: &TemplateLoader) -> Result<String> {
    let template_list = templates.list_templates()?;
    let mut hasher = blake3::Hasher::new();

    // Include moss version in the template hash input so schema/template parsing changes
    // can't accidentally reuse an old cache.
    hasher.update(moss_version_string().as_bytes());

    // Hash each offering's effective config in stable order.
    let mut template_list = template_list;
    template_list.sort_by(|a, b| a.name.cmp(&b.name));
    for t in template_list {
        let template = templates.load(&t.name)?;
        let payload = serde_json::json!({
            "name": t.name,
            "category": t.category,
            "description": t.description,
            "tags": t.tags,
            "image": template.image,
            "ports": template.ports,
            "environment": template.environment,
            "volumes": template.volumes,
            "compatibility": template.compatibility,
        });
        hasher.update(serde_json::to_vec(&payload).unwrap_or_default().as_slice());
    }

    Ok(hasher.finalize().to_hex().to_string())
}

/// Ensure offerings index is loaded (with caching)
///
/// Loads offerings index from cache or rebuilds if:
/// - Cache doesn't exist
/// - force_rebuild is true
/// - Fingerprint doesn't match (version/capabilities/templates changed)
///
/// # Parameters
/// - `state`: Application state with templates and offerings_index
/// - `force_rebuild`: Skip cache and force rebuild
///
/// # Composability
/// This function manages AppState's offerings_index cache.
/// It delegates to:
/// - `load_offerings_cache()` for disk persistence (infra layer)
/// - `rebuild_offerings_index()` for index generation (domain layer)
/// - `save_offerings_cache()` for disk persistence (infra layer)
pub async fn ensure_offerings_index(
    state: &crate::AppState,
    force_rebuild: bool,
) -> Result<()> {
    if !force_rebuild {
        let existing = state.offerings_index.read().await;
        if existing.is_some() {
            return Ok(());
        }
    }

    // Try disk cache first (best-effort)
    if !force_rebuild {
        if let Some(on_disk) = crate::infra::load_offerings_cache::<OfferingsIndexCache>().await? {
            let current = OfferingsFingerprint {
                moss_version: moss_version_string(),
                capabilities_hash: current_capabilities_hash(),
                templates_hash: templates_hash(&state.templates).await?,
            };

            if on_disk.fingerprint == current {
                *state.offerings_index.write().await = Some(on_disk);
                return Ok(());
            }
        }
    }

    let rebuilt = rebuild_offerings_index(&state.templates).await?;
    crate::infra::save_offerings_cache(&rebuilt).await?;
    *state.offerings_index.write().await = Some(rebuilt);
    Ok(())
}

/// Get a compiled offering by name
///
/// Ensures offerings index is loaded, then queries for specific offering.
///
/// # Returns
/// - `Ok(Some(offering))`: Offering found
/// - `Ok(None)`: Offering not found
/// - `Err(_)`: Failed to load offerings index
///
/// # Composability
/// This function ensures index is loaded before querying.
/// Delegates to `ensure_offerings_index()` for cache management.
pub async fn get_compiled_offering(
    state: &crate::AppState,
    offering: &str,
) -> Result<Option<CompiledOffering>> {
    ensure_offerings_index(state, false).await?;
    let guard = state.offerings_index.read().await;
    Ok(guard
        .as_ref()
        .and_then(|idx| idx.offerings.iter().find(|o| o.name == offering).cloned()))
}

/// Rebuild offerings index from templates
///
/// Evaluates compatibility rules and compiles all offerings.
/// Returns cache-ready index with fingerprint.
pub async fn rebuild_offerings_index(templates: &TemplateLoader) -> Result<OfferingsIndexCache> {
    let mut template_list = templates.list_templates()?;
    template_list.sort_by(|a, b| a.name.cmp(&b.name));

    let fingerprint = OfferingsFingerprint {
        moss_version: moss_version_string(),
        capabilities_hash: current_capabilities_hash(),
        templates_hash: templates_hash(templates).await?,
    };

    let mut offerings = Vec::with_capacity(template_list.len());
    for t in template_list {
        let mut template = templates.load(&t.name)?;
        let compatibility = compile_compatibility(&mut template);

        offerings.push(CompiledOffering {
            name: t.name,
            category: t.category,
            description: t.description,
            tags: t.tags,
            image: template.image,
            ports: template.ports,
            environment: template.environment,
            volumes: template.volumes,
            compatibility,
        });
    }

    Ok(OfferingsIndexCache {
        fingerprint,
        generated_at: chrono::Utc::now().to_rfc3339(),
        offerings,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_moss_version_string() {
        let version = moss_version_string();
        assert!(version.contains('.'));
    }

    #[test]
    fn test_capabilities_hash_stable() {
        let hash1 = current_capabilities_hash();
        let hash2 = current_capabilities_hash();
        // Hash should be stable for same capabilities
        assert_eq!(hash1, hash2);
        assert!(!hash1.is_empty());
    }

    #[test]
    fn test_fingerprint_equality() {
        let fp1 = OfferingsFingerprint {
            moss_version: "1.0.0".into(),
            capabilities_hash: "abc123".into(),
            templates_hash: "def456".into(),
        };
        let fp2 = fp1.clone();
        assert_eq!(fp1, fp2);
    }
}
