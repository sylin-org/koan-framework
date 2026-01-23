//! Compatibility evaluation and binary validation
//!
//! Pure business logic for:
//! - Hardware capability detection (CPU, memory, GPU/AI runtimes)
//! - Compatibility rule evaluation
//! - Binary architecture validation
//!
//! No I/O here - delegates to metrics module for detection.

use anyhow::Result;

/// Hardware capabilities for compatibility checking
#[derive(Debug, Clone)]
pub struct CompatCheckCapabilities {
    pub cpu_model: Option<String>,
    pub cpu_features: Option<Vec<String>>,
    pub architecture: Option<String>,
    pub total_memory_mb: Option<u64>,

    // GPU/AI capabilities
    pub has_cuda: bool,
    pub has_rocm: bool,
    pub has_directml: bool,
    pub has_openvino: bool,
    pub gpu_vram_total_mb: u64,
}

/// Result of compatibility evaluation
#[derive(Debug, Clone)]
pub enum CompatibilityDecision {
    Pass,
    Fallback { image: String, reason: String },
    Warning { reason: String, suggestion: Option<String> },
    Fail { reason: String, suggestion: Option<String> },
}

/// Compiled compatibility result (serializable)
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct CompiledCompatibility {
    pub decision: String, // "pass" | "fallback" | "warning" | "fail"
    #[serde(skip_serializing_if = "Option::is_none")]
    pub reason: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub original_image: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fallback_image: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub suggestion: Option<String>,
}

/// Get current hardware capabilities for compatibility checking
///
/// Delegates to metrics module for actual detection.
pub fn get_current_compat_capabilities() -> CompatCheckCapabilities {
    let (cpu_model, cpu_features, architecture) = crate::metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));
    let resources = crate::metrics::collect_stone_resources().ok();
    let total_memory_mb = resources.as_ref().map(|r| r.memory.total_bytes / 1024 / 1024);

    // Detect GPU/AI capabilities using new ai_runtimes format
    let gpus = crate::metrics::detect_gpus();

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
    let gpu_vram_total_mb: u64 = gpus.iter().filter_map(|g| g.vram_mb).sum();

    CompatCheckCapabilities {
        cpu_model: Some(cpu_model),
        cpu_features: Some(cpu_features),
        architecture: Some(architecture),
        total_memory_mb,
        has_cuda,
        has_rocm,
        has_directml,
        has_openvino,
        gpu_vram_total_mb,
    }
}

/// Compile compatibility rules for a template
///
/// Evaluates rules and modifies template image if needed (fallback).
/// Returns structured compatibility result.
pub fn compile_compatibility(
    template: &mut crate::templates::ServiceTemplate,
) -> CompiledCompatibility {
    if let Some(rules) = &template.compatibility {
        let capabilities = get_current_compat_capabilities();
        match evaluate_compatibility(rules, &capabilities) {
            CompatibilityDecision::Pass => CompiledCompatibility {
                decision: garden_common::COMPAT_PASS.to_string(),
                reason: None,
                original_image: None,
                fallback_image: None,
                suggestion: None,
            },
            CompatibilityDecision::Fallback { image, reason } => {
                let original_image = template.image.clone();
                template.image = image.clone();
                CompiledCompatibility {
                    decision: garden_common::COMPAT_FALLBACK.to_string(),
                    reason: Some(reason),
                    original_image: Some(original_image),
                    fallback_image: Some(image),
                    suggestion: None,
                }
            }
            CompatibilityDecision::Warning { reason, suggestion } => CompiledCompatibility {
                decision: garden_common::COMPAT_WARNING.to_string(),
                reason: Some(reason),
                original_image: None,
                fallback_image: None,
                suggestion,
            },
            CompatibilityDecision::Fail { reason, suggestion } => CompiledCompatibility {
                decision: garden_common::COMPAT_FAIL.to_string(),
                reason: Some(reason),
                original_image: Some(template.image.clone()),
                fallback_image: None,
                suggestion,
            },
        }
    } else {
        CompiledCompatibility {
            decision: garden_common::COMPAT_PASS.to_string(),
            reason: None,
            original_image: None,
            fallback_image: None,
            suggestion: None,
        }
    }
}

/// Evaluate compatibility rules against current capabilities
///
/// First matching rule wins (AND semantics within a rule).
pub fn evaluate_compatibility(
    rules: &garden_common::CompatibilityRules,
    capabilities: &CompatCheckCapabilities,
) -> CompatibilityDecision {
    // Evaluate each rule in order (first match wins). A rule matches only if
    // all specified condition fields match (AND semantics).
    for rule in &rules.compatibility_rules {
        let condition = &rule.condition;
        let mut matches = true;

        if let Some(models) = &condition.processor_models {
            // Exact match against CPU model string (case-sensitive, since most
            // model strings are already normalized by the source).
            let ok = capabilities
                .cpu_model
                .as_ref()
                .map(|cpu_model| models.iter().any(|model| cpu_model == model))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(patterns) = &condition.processor_patterns {
            let ok = capabilities
                .cpu_model
                .as_ref()
                .map(|cpu_model| patterns.iter().any(|pattern| cpu_model.contains(pattern)))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(required_missing) = &condition.cpu_features_missing {
            // Match when any of the listed features are missing.
            let ok = capabilities
                .cpu_features
                .as_ref()
                .map(|cpu_features| required_missing.iter().any(|f| !cpu_features.contains(f)))
                // If we couldn't detect CPU features, don't assume they're missing.
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(architectures) = &condition.architectures {
            let ok = capabilities
                .architecture
                .as_ref()
                .map(|arch| architectures.contains(arch))
                .unwrap_or(false);
            matches &= ok;
        }

        if let Some(max_memory_mb) = condition.memory_mb_less_than {
            let ok = capabilities
                .total_memory_mb
                .map(|total_memory| total_memory < max_memory_mb)
                .unwrap_or(false);
            matches &= ok;
        }

        // AI/GPU capability checks
        if let Some(requires_ai_any) = &condition.requires_ai_any {
            // Match if ANY of the specified runtimes are present (OR logic)
            let has_match = requires_ai_any.iter().any(|runtime| {
                match runtime.to_lowercase().as_str() {
                    "cuda" => capabilities.has_cuda,
                    "rocm" => capabilities.has_rocm,
                    "directml" => capabilities.has_directml,
                    "openvino" => capabilities.has_openvino,
                    _ => false,
                }
            });
            matches &= has_match;
        }

        if let Some(requires_ai_all) = &condition.requires_ai_all {
            // Match if ALL of the specified runtimes are present (AND logic)
            let has_all = requires_ai_all.iter().all(|runtime| {
                match runtime.to_lowercase().as_str() {
                    "cuda" => capabilities.has_cuda,
                    "rocm" => capabilities.has_rocm,
                    "directml" => capabilities.has_directml,
                    "openvino" => capabilities.has_openvino,
                    _ => false,
                }
            });
            matches &= has_all;
        }

        if let Some(min_vram_mb) = condition.vram_mb_at_least {
            matches &= capabilities.gpu_vram_total_mb >= min_vram_mb;
        }

        if let Some(max_vram_mb) = condition.vram_mb_less_than {
            matches &= capabilities.gpu_vram_total_mb < max_vram_mb;
        }

        if matches {
            if let Some(fallback) = &rule.fallback {
                return CompatibilityDecision::Fallback {
                    image: fallback.image.clone(),
                    reason: rule.reason.clone(),
                };
            }

            // If warn_only is set, return Warning instead of Fail
            if rule.warn_only {
                return CompatibilityDecision::Warning {
                    reason: rule.reason.clone(),
                    suggestion: rule.suggestion.clone(),
                };
            }

            return CompatibilityDecision::Fail {
                reason: rule.reason.clone(),
                suggestion: rule.suggestion.clone(),
            };
        }
    }

    CompatibilityDecision::Pass
}

/// Validate ELF binary architecture matches system
///
/// Returns the detected architecture or an error if validation fails.
pub fn validate_binary_architecture(binary_data: &[u8]) -> Result<String> {
    use anyhow::bail;

    // ELF header structure:
    // 0x00-03: Magic (\x7fELF)
    // 0x04: Class (1=32-bit, 2=64-bit)
    // 0x12-13: Machine type (little-endian u16)

    if binary_data.len() < 20 {
        bail!("Binary too small (expected at least 20 bytes for ELF header)");
    }

    if &binary_data[0..4] != b"\x7fELF" {
        bail!("Not a valid ELF binary (invalid magic bytes)");
    }

    let machine_type = u16::from_le_bytes([binary_data[0x12], binary_data[0x13]]);
    let arch = match machine_type {
        0x3E => "x86_64",
        0xB7 => "aarch64",
        0x28 => "arm",
        _ => bail!("Unsupported architecture: machine type {:#x}", machine_type),
    };

    // Compare with system architecture
    let system_arch = std::env::consts::ARCH;
    if arch != system_arch {
        bail!(
            "Architecture mismatch: binary is {}, but system is {}",
            arch,
            system_arch
        );
    }

    Ok(arch.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_evaluate_compatibility_pass() {
        let rules = garden_common::CompatibilityRules {
            version: "1.0".to_string(),
            compatibility_rules: vec![],
            post_install_healthcheck: None,
        };
        let caps = CompatCheckCapabilities {
            cpu_model: Some("Intel Core i7".into()),
            cpu_features: Some(vec!["avx2".into()]),
            architecture: Some("x86_64".into()),
            total_memory_mb: Some(16384),
            has_cuda: false,
            has_rocm: false,
            has_directml: false,
            has_openvino: false,
            gpu_vram_total_mb: 0,
        };

        let result = evaluate_compatibility(&rules, &caps);
        assert!(matches!(result, CompatibilityDecision::Pass));
    }

    #[test]
    fn test_validate_binary_architecture_valid() {
        // Minimal valid ELF header for x86_64
        let mut header = vec![0u8; 20];
        header[0..4].copy_from_slice(b"\x7fELF");
        header[0x12] = 0x3E; // x86_64 machine type (little-endian)
        header[0x13] = 0x00;

        let result = validate_binary_architecture(&header);
        if std::env::consts::ARCH == "x86_64" {
            assert!(result.is_ok());
        } else {
            assert!(result.is_err());
        }
    }

    #[test]
    fn test_validate_binary_architecture_invalid_magic() {
        let header = vec![0u8; 20];
        let result = validate_binary_architecture(&header);
        assert!(result.is_err());
    }
}
