//! Template/offering management
//!
//! Loads and manages service offering templates

use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Service offering template
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OfferingTemplate {
    pub name: String,
    pub version: String,
    pub image: String,
    pub ports: HashMap<String, u16>,
    pub environment: Option<HashMap<String, String>>,
    pub volumes: Option<Vec<String>>,
    pub compatibility_rules: Option<serde_json::Value>,
}

/// Template manager
pub struct TemplateManager {
    templates: HashMap<String, OfferingTemplate>,
}

impl TemplateManager {
    /// Create a new template manager
    pub fn new() -> Self {
        Self {
            templates: HashMap::new(),
        }
    }

    /// Load templates from directory
    pub async fn load_templates(&mut self, _templates_dir: &std::path::Path) -> Result<()> {
        // TODO: Implement template loading from filesystem
        // For now, return OK
        Ok(())
    }

    /// Get a template by name
    pub fn get_template(&self, name: &str) -> Option<&OfferingTemplate> {
        self.templates.get(name)
    }

    /// List all available templates
    pub fn list_templates(&self) -> Vec<String> {
        self.templates.keys().cloned().collect()
    }

    /// Register a template
    pub fn register_template(&mut self, template: OfferingTemplate) {
        self.templates.insert(template.name.clone(), template);
    }
}

impl Default for TemplateManager {
    fn default() -> Self {
        Self::new()
    }
}
