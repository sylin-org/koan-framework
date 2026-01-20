use anyhow::{Context, Result};
use serde::Deserialize;
use std::collections::HashMap;
use std::path::Path;
use garden_common::CompatibilityRules;

#[cfg(target_os = "windows")]
pub const RUNTIME_TEMPLATES_DIR: &str = "C:\\ProgramData\\ZenGarden\\templates";

#[cfg(not(target_os = "windows"))]
pub const RUNTIME_TEMPLATES_DIR: &str = "/etc/zen-garden/templates";

#[derive(Debug, Deserialize)]
struct ComposeFile {
    services: HashMap<String, ServiceConfig>,
}

#[derive(Debug, Deserialize, Clone)]
struct ServiceConfig {
    image: String,
    #[serde(default)]
    ports: Vec<String>,
    #[serde(default)]
    environment: Option<serde_yaml::Value>,  // Support both list and map formats
    #[serde(default)]
    volumes: Vec<String>,
}

#[derive(Debug, Clone, serde::Deserialize)]
struct Frontmatter {
    #[serde(default)]
    description: Option<String>,
    #[serde(default)]
    category: Option<String>,
    #[serde(default)]
    tags: Option<Vec<String>>,
}

pub struct ServiceTemplate {
    pub image: String,
    pub ports: Vec<(u16, u16)>, // (host_port, container_port)
    pub environment: Vec<String>,
    pub volumes: Vec<(String, String)>, // (host_path, container_path)
    pub compatibility: Option<CompatibilityRules>,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct TemplateInfo {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
}

pub struct TemplateLoader {
    runtime_templates_dir: Option<std::path::PathBuf>,
}

// Debug structures for template inspection endpoints (not currently used)
#[allow(dead_code)]
#[derive(Debug, Clone, serde::Serialize)]
pub struct PathProbe {
    pub path: String,
    pub exists: bool,
}

#[allow(dead_code)]
#[derive(Debug, Clone, serde::Serialize)]
pub struct TemplateSourcesDebug {
    pub runtime_templates_dir: Option<String>,
    pub runtime_templates_active: bool,
    pub snippet_candidates: Vec<PathProbe>,
    pub compatibility_candidates: Vec<PathProbe>,
}

impl TemplateLoader {
    pub fn new() -> Self {
        // Check for runtime templates directory
        let templates_dir = Path::new(RUNTIME_TEMPLATES_DIR);
        let runtime_templates_dir = if templates_dir.exists() {
            // Note: Console event emitted from main.rs where console printer is available
            Some(templates_dir.to_path_buf())
        } else {
            tracing::warn!("Runtime templates directory missing: {}", RUNTIME_TEMPLATES_DIR);
            None
        };

        Self { runtime_templates_dir }
    }

    /// List all available templates with their categories and descriptions
    pub fn list_templates(&self) -> Result<Vec<TemplateInfo>> {
        let dir = self
            .runtime_templates_dir
            .as_ref()
            .context(format!("Runtime templates directory not found: {}", RUNTIME_TEMPLATES_DIR))?;

        if !self.has_runtime_templates(dir) {
            anyhow::bail!(
                "Runtime templates directory exists but contains no templates: {}",
                dir.display()
            );
        }

        let templates = self.collect_runtime_templates(dir)?;
        // Console event: Manifests | FOUND emitted from main.rs
        Ok(templates)
    }

    /// Get raw template YAML content by name
    pub fn get_template_content(&self, offering: &str) -> Result<String> {
        let dir = self
            .runtime_templates_dir
            .as_ref()
            .context(format!("Runtime templates directory not found: {}", RUNTIME_TEMPLATES_DIR))?;

        if !self.has_runtime_templates(dir) {
            anyhow::bail!(
                "Runtime templates directory exists but contains no templates: {}",
                dir.display()
            );
        }

        self.load_from_runtime_filesystem(dir, offering)
    }

    fn collect_runtime_templates(&self, dir: &std::path::Path) -> Result<Vec<TemplateInfo>> {
        let mut templates = Vec::new();
        let categories = ["data", "messaging", "ai", "vector", "secrets", "observability", "cache"];

        for category in &categories {
            let category_dir = dir.join(category);
            if !category_dir.exists() {
                continue;
            }

            if let Ok(entries) = std::fs::read_dir(&category_dir) {
                for entry in entries.filter_map(Result::ok) {
                    let path = entry.path();
                    if path.extension().and_then(|s| s.to_str()) == Some("yaml") {
                        if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                            if name.ends_with(".snippet.yaml") {
                                let offering_name = name.trim_end_matches(".snippet.yaml");
                                let frontmatter = self.load_frontmatter(&category_dir, offering_name);

                                let resolved_category = frontmatter
                                    .as_ref()
                                    .and_then(|f| f.category.as_ref())
                                    .cloned()
                                    .unwrap_or_else(|| category.to_string());

                                let resolved_description = frontmatter
                                    .as_ref()
                                    .and_then(|f| f.description.as_ref())
                                    .cloned()
                                    .unwrap_or_else(|| self.extract_description(offering_name));

                                let resolved_tags = frontmatter
                                    .as_ref()
                                    .and_then(|f| f.tags.as_ref())
                                    .cloned()
                                    .unwrap_or_default()
                                    .into_iter()
                                    .map(|t| t.trim().to_lowercase())
                                    .filter(|t| !t.is_empty())
                                    .collect::<Vec<_>>();

                                templates.push(TemplateInfo {
                                    name: offering_name.to_string(),
                                    category: resolved_category,
                                    description: resolved_description,
                                    tags: resolved_tags,
                                });
                            }
                        }
                    }
                }
            }
        }

        templates.sort_by(|a, b| a.name.cmp(&b.name));
        Ok(templates)
    }

    fn extract_description(&self, offering: &str) -> String {
        // Fallback description when frontmatter is missing
        // All services should have proper frontmatter with descriptions
        format!("{} service", offering)
    }

    fn load_frontmatter(&self, category_dir: &std::path::Path, offering: &str) -> Option<Frontmatter> {
        let filename = format!("{}.frontmatter.json", offering);
        let path = category_dir.join(filename);
        let content = std::fs::read_to_string(path).ok()?;
        serde_json::from_str::<Frontmatter>(&content).ok()
    }

    pub fn load(&self, offering: &str) -> Result<ServiceTemplate> {
        let dir = self
            .runtime_templates_dir
            .as_ref()
            .context(format!("Runtime templates directory not found: {}", RUNTIME_TEMPLATES_DIR))?;

        if !self.has_runtime_templates(dir) {
            anyhow::bail!(
                "Runtime templates directory exists but contains no templates: {}",
                dir.display()
            );
        }

        let yaml = self
            .load_from_runtime_filesystem(dir, offering)
            .context(format!("Template '{}' not found in runtime filesystem", offering))?;
        // Console event: Manifests | LOADED emitted from main.rs

        let compatibility = self.load_compatibility_from_runtime(offering);
        self.parse_template(offering, &yaml, compatibility)
    }

    #[allow(dead_code)]
    pub fn debug_sources(&self, offering: &str) -> TemplateSourcesDebug {
        let filename_snippet = format!("{}.snippet.yaml", offering);
        let filename_compat = format!("{}.compatibility.yaml", offering);
        let categories = [
            "data",
            "messaging",
            "ai",
            "vector",
            "secrets",
            "observability",
            "cache",
            "templates",
        ];

        let runtime_templates_dir = self.runtime_templates_dir.as_ref().map(|p| p.display().to_string());
        let runtime_templates_active = self
            .runtime_templates_dir
            .as_ref()
            .map(|p| self.has_runtime_templates(p))
            .unwrap_or(false);

        let mut snippet_candidates = Vec::new();
        let mut compatibility_candidates = Vec::new();

        if let Some(ref dir) = self.runtime_templates_dir {
            for category in &categories {
                let snippet_path = dir.join(category).join(&filename_snippet);
                snippet_candidates.push(PathProbe {
                    path: snippet_path.display().to_string(),
                    exists: snippet_path.exists(),
                });

                let compat_path = dir.join(category).join(&filename_compat);
                compatibility_candidates.push(PathProbe {
                    path: compat_path.display().to_string(),
                    exists: compat_path.exists(),
                });
            }

            let root_snippet = dir.join(&filename_snippet);
            snippet_candidates.push(PathProbe {
                path: root_snippet.display().to_string(),
                exists: root_snippet.exists(),
            });

            let root_compat = dir.join(&filename_compat);
            compatibility_candidates.push(PathProbe {
                path: root_compat.display().to_string(),
                exists: root_compat.exists(),
            });
        }

        TemplateSourcesDebug {
            runtime_templates_dir,
            runtime_templates_active,
            snippet_candidates,
            compatibility_candidates,
        }
    }

    fn has_runtime_templates(&self, dir: &std::path::Path) -> bool {
        let categories = ["data", "messaging", "ai", "vector", "secrets", "observability", "cache", "templates"];
        
        for category in &categories {
            let category_dir = dir.join(category);
            if category_dir.exists() {
                if let Ok(entries) = std::fs::read_dir(&category_dir) {
                    if entries.filter_map(Result::ok)
                        .any(|e| e.path().extension().and_then(|s| s.to_str()) == Some("yaml")) {
                        return true;
                    }
                }
            }
        }
        
        // Also check root level
        if let Ok(entries) = std::fs::read_dir(dir) {
            if entries.filter_map(Result::ok)
                .any(|e| e.path().extension().and_then(|s| s.to_str()) == Some("yaml")) {
                return true;
            }
        }
        
        false
    }

    fn load_from_runtime_filesystem(&self, dir: &std::path::Path, offering: &str) -> Result<String> {
        // Try finding {offering}.snippet.yaml in subdirectories
        let filename = format!("{}.snippet.yaml", offering);
        let categories = ["data", "messaging", "ai", "vector", "secrets", "observability", "cache", "templates"];
        
        for category in &categories {
            let path = dir.join(category).join(&filename);
            if path.exists() {
                return std::fs::read_to_string(&path)
                    .context(format!("Failed to read template file: {}", path.display()));
            }
        }
        
        // Also try root level
        let root_path = dir.join(&filename);
        if root_path.exists() {
            return std::fs::read_to_string(&root_path)
                .context(format!("Failed to read template file: {}", root_path.display()));
        }

        anyhow::bail!("Template file not found in runtime filesystem")
    }

    fn parse_template(&self, service_name: &str, yaml: &str, compatibility: Option<CompatibilityRules>) -> Result<ServiceTemplate> {
        tracing::debug!(service = service_name, yaml_len = yaml.len(), "Parsing template");
        
        // Strip Windows CRLF line endings (convert \r\n to \n)
        let yaml = yaml.replace("\r\n", "\n");
        
        // Try parsing as snippet format first (direct service config)
        match serde_yaml::from_str::<ServiceConfig>(&yaml) {
            Ok(service_config) => {
                // Console event: Manifests | PARSED emitted from main.rs
                return Ok(self.service_config_to_template(service_config, compatibility));
            }
            Err(e) => {
                tracing::debug!(service = service_name, error = ?e, "Snippet parse failed, trying compose format");
            }
        }

        // Fallback: try parsing as compose file (legacy format with services: wrapper)
        let compose: ComposeFile =
            serde_yaml::from_str(&yaml).context(format!("Failed to parse YAML as snippet or compose file. First 100 chars: {}", &yaml[..yaml.len().min(100)]))?;

        let service_config = compose
            .services
            .get(service_name)
            .context(format!("Service '{}' not found in compose file", service_name))?
            .clone();

        Ok(self.service_config_to_template(service_config, compatibility))
    }

    fn service_config_to_template(&self, service_config: ServiceConfig, compatibility: Option<CompatibilityRules>) -> ServiceTemplate {
        // Parse ports (format: "host:container" or "container")
        let ports = service_config
            .ports
            .iter()
            .filter_map(|p| {
                let parts: Vec<&str> = p.split(':').collect();
                match parts.len() {
                    2 => {
                        let host = parts[0].parse::<u16>().ok()?;
                        let container = parts[1].parse::<u16>().ok()?;
                        Some((host, container))
                    }
                    1 => {
                        let port = parts[0].parse::<u16>().ok()?;
                        Some((port, port))
                    }
                    _ => None,
                }
            })
            .collect();

        // Parse environment variables - support both list and map formats
        let environment = match &service_config.environment {
            Some(serde_yaml::Value::Sequence(list)) => {
                // List format: ["KEY=VALUE", ...]
                list.iter()
                    .filter_map(|v| v.as_str().map(|s| s.to_string()))
                    .collect()
            }
            Some(serde_yaml::Value::Mapping(map)) => {
                // Map format: {KEY: VALUE, ...}
                map.iter()
                    .filter_map(|(k, v)| {
                        let key = k.as_str()?;
                        let value = v.as_str().unwrap_or(""); // Empty string for non-string values
                        Some(format!("{}={}", key, value))
                    })
                    .collect()
            }
            _ => Vec::new(),
        };

        // Parse volumes (format: "host:container" or "volume_name:container")
        let volumes = service_config
            .volumes
            .iter()
            .filter_map(|v| {
                let parts: Vec<&str> = v.split(':').collect();
                if parts.len() == 2 {
                    // For named volumes, use platform-specific base path
                    let host_path = if parts[0].starts_with('/') || parts[0].contains('\\') {
                        parts[0].to_string()
                    } else {
                        #[cfg(target_os = "windows")]
                        let base = "C:\\ProgramData\\ZenGarden\\volumes";
                        #[cfg(not(target_os = "windows"))]
                        let base = "/var/lib/zen-garden/volumes";
                        
                        format!("{}\\{}", base, parts[0])
                    };
                    Some((host_path, parts[1].to_string()))
                } else {
                    None
                }
            })
            .collect();

        ServiceTemplate {
            image: service_config.image.clone(),
            ports,
            environment,
            volumes,
            compatibility,
        }
    }

    /// Load compatibility rules from runtime filesystem
    fn load_compatibility_from_runtime(&self, service_name: &str) -> Option<CompatibilityRules> {
        let filename = format!("{}.compatibility.yaml", service_name);
        let categories = ["data", "messaging", "ai", "vector", "secrets", "observability", "cache", "templates"];
        
        // Try each category directory
        for category in &categories {
            let path = Path::new(RUNTIME_TEMPLATES_DIR).join(category).join(&filename);
            
            if let Ok(yaml) = std::fs::read_to_string(&path) {
                match serde_yaml::from_str::<CompatibilityRules>(&yaml) {
                    Ok(rules) => {
                        return Some(rules);
                    }
                    Err(e) => {
                        tracing::warn!(service = service_name, path = %path.display(), error = ?e, "Failed to parse compatibility rules");
                        return None;
                    }
                }
            }
        }
        
        tracing::debug!(service = service_name, "No runtime compatibility rules found");
        None
    }
}
