//! Category configuration for manifest folders
//!
//! Loads category.json files from manifest directories to provide
//! data-driven category semantics (aliases, protocols, templates).

use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::Path;
use std::sync::OnceLock;

/// Category configuration loaded from category.json
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CategoryConfig {
    /// Canonical category name (matches folder name)
    pub name: String,
    /// Human-readable description
    pub description: String,
    /// Token aliases that resolve to this category
    #[serde(default)]
    pub aliases: Vec<String>,
    /// Categories this category encompasses (e.g., "database" includes "cache")
    #[serde(default)]
    pub parent_of: Vec<String>,
    /// Default connection protocol for services in this category
    #[serde(default)]
    pub default_protocol: Option<String>,
    /// Default connection URI template
    #[serde(default)]
    pub connection_template: Option<String>,
    /// Default tags for offerings in this category
    #[serde(default)]
    pub tags: Vec<String>,
}

/// Registry of loaded category configurations
#[derive(Debug, Clone, Default)]
pub struct CategoryRegistry {
    /// Map of canonical category name to config
    categories: HashMap<String, CategoryConfig>,
    /// Map of alias to canonical category name
    alias_map: HashMap<String, String>,
    /// Map of parent category to child categories
    parent_map: HashMap<String, Vec<String>>,
}

impl CategoryRegistry {
    /// Create a new empty registry
    pub fn new() -> Self {
        Self::default()
    }

    /// Register a category configuration
    pub fn register(&mut self, config: CategoryConfig) {
        let name = config.name.clone();

        // Register aliases
        for alias in &config.aliases {
            self.alias_map.insert(alias.to_lowercase(), name.clone());
        }
        // Also register the name itself as an alias
        self.alias_map.insert(name.to_lowercase(), name.clone());

        // Register parent relationships
        if !config.parent_of.is_empty() {
            self.parent_map.insert(name.clone(), config.parent_of.clone());
        }

        self.categories.insert(name, config);
    }

    /// Get all known category names
    pub fn category_names(&self) -> Vec<&str> {
        self.categories.keys().map(|s| s.as_str()).collect()
    }

    /// Get category config by name
    pub fn get(&self, name: &str) -> Option<&CategoryConfig> {
        self.categories.get(name)
    }

    /// Resolve a token to its canonical category name
    pub fn resolve_token(&self, token: &str) -> Option<&str> {
        self.alias_map.get(&token.to_lowercase()).map(|s| s.as_str())
    }

    /// Check if a token matches a category (including parent relationships)
    ///
    /// For example, if "data" is parent_of ["cache", "search", "vector"],
    /// then `token_matches("data", "cache")` returns true.
    pub fn token_matches(&self, token: &str, category: &str) -> bool {
        let token_lower = token.to_lowercase();
        let category_lower = category.to_lowercase();

        // Direct match
        if token_lower == category_lower {
            return true;
        }

        // Resolve token to canonical category
        if let Some(resolved) = self.alias_map.get(&token_lower) {
            if resolved.to_lowercase() == category_lower {
                return true;
            }
            // Check parent relationship
            if let Some(children) = self.parent_map.get(resolved) {
                if children.iter().any(|c| c.to_lowercase() == category_lower) {
                    return true;
                }
            }
        }

        false
    }

    /// Get default protocol for a category
    pub fn default_protocol(&self, category: &str) -> Option<&str> {
        self.categories
            .get(category)
            .and_then(|c| c.default_protocol.as_deref())
    }

    /// Get connection template for a category
    pub fn connection_template(&self, category: &str) -> Option<&str> {
        self.categories
            .get(category)
            .and_then(|c| c.connection_template.as_deref())
    }
}

/// Global category registry (loaded once)
static CATEGORY_REGISTRY: OnceLock<CategoryRegistry> = OnceLock::new();

/// Load category configurations from a manifests directory
///
/// Scans subdirectories for category.json files and builds the registry.
pub fn load_categories<P: AsRef<Path>>(manifests_dir: P) -> Result<CategoryRegistry, std::io::Error> {
    let mut registry = CategoryRegistry::new();
    let dir = manifests_dir.as_ref();

    if !dir.exists() {
        return Ok(registry);
    }

    for entry in std::fs::read_dir(dir)? {
        let entry = entry?;
        let path = entry.path();

        if path.is_dir() {
            let config_path = path.join("category.json");
            if config_path.exists() {
                match std::fs::read_to_string(&config_path) {
                    Ok(content) => {
                        match serde_json::from_str::<CategoryConfig>(&content) {
                            Ok(config) => {
                                registry.register(config);
                            }
                            Err(e) => {
                                eprintln!(
                                    "Warning: Failed to parse {}: {}",
                                    config_path.display(),
                                    e
                                );
                            }
                        }
                    }
                    Err(e) => {
                        eprintln!(
                            "Warning: Failed to read {}: {}",
                            config_path.display(),
                            e
                        );
                    }
                }
            }
        }
    }

    Ok(registry)
}

/// Get or initialize the global category registry
///
/// Loads from the default manifests directory on first call.
/// Returns a static reference for subsequent calls.
pub fn get_category_registry() -> &'static CategoryRegistry {
    CATEGORY_REGISTRY.get_or_init(|| {
        // Try common manifest locations
        let paths = [
            "manifests",
            "../manifests",
            "../../manifests",
        ];

        for path in paths {
            if let Ok(registry) = load_categories(path) {
                if !registry.categories.is_empty() {
                    return registry;
                }
            }
        }

        // Return empty registry if no manifests found
        CategoryRegistry::new()
    })
}

/// Initialize the category registry from a specific directory
///
/// Should be called once at startup if using a non-default manifest location.
pub fn init_category_registry<P: AsRef<Path>>(manifests_dir: P) -> Result<(), String> {
    let registry = load_categories(manifests_dir)
        .map_err(|e| format!("Failed to load categories: {}", e))?;

    CATEGORY_REGISTRY
        .set(registry)
        .map_err(|_| "Category registry already initialized".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_category_registry() {
        let mut registry = CategoryRegistry::new();

        registry.register(CategoryConfig {
            name: "data".to_string(),
            description: "Databases".to_string(),
            aliases: vec!["database".to_string(), "db".to_string()],
            parent_of: vec!["cache".to_string(), "search".to_string()],
            default_protocol: Some("tcp".to_string()),
            connection_template: None,
            tags: vec![],
        });

        registry.register(CategoryConfig {
            name: "cache".to_string(),
            description: "Caching".to_string(),
            aliases: vec!["caching".to_string()],
            parent_of: vec![],
            default_protocol: Some("redis".to_string()),
            connection_template: Some("redis://{host}:{port}".to_string()),
            tags: vec![],
        });

        // Test alias resolution
        assert_eq!(registry.resolve_token("db"), Some("data"));
        assert_eq!(registry.resolve_token("database"), Some("data"));
        assert_eq!(registry.resolve_token("caching"), Some("cache"));

        // Test token matching
        assert!(registry.token_matches("data", "data"));
        assert!(registry.token_matches("db", "data"));
        assert!(registry.token_matches("data", "cache")); // parent_of
        assert!(!registry.token_matches("cache", "data")); // not reverse

        // Test default protocol
        assert_eq!(registry.default_protocol("data"), Some("tcp"));
        assert_eq!(registry.default_protocol("cache"), Some("redis"));
    }
}
