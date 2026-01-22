//! Template command - browse and display service templates
//!
//! Commands for working with service templates:
//! - list: List all available templates
//! - show: Show details for a specific template

use crate::command_manifest::cmd;
use crate::commands::{Command, CommandResult};
use crate::context::CommandContext;
use crate::suggestions;
use crate::ui;
use async_trait::async_trait;
use garden_common::GardenApiResponse;
use std::collections::HashMap;

/// Template info from API
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct TemplateInfo {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
}

/// Template command action
pub enum TemplateAction {
    /// List all available templates
    List,
    /// Show details for a specific template
    Show { name: String },
}

/// Template command for browsing service templates
pub struct TemplateCommand {
    pub action: TemplateAction,
    pub quiet_mode: bool,
}

impl TemplateCommand {
    pub fn new(action: TemplateAction, quiet_mode: bool) -> Self {
        Self { action, quiet_mode }
    }
}

#[async_trait]
impl Command for TemplateCommand {
    async fn execute(&self, ctx: &CommandContext) -> CommandResult {
        let endpoint = ctx.endpoint()?;

        match &self.action {
            TemplateAction::List => {
                list_templates(&ctx.client, endpoint).await?;
            }
            TemplateAction::Show { name } => {
                show_template(&ctx.client, endpoint, name).await?;
            }
        }

        // Self-teaching suggestions
        suggestions::print_suggestions(cmd::TEMPLATE, self.quiet_mode);

        Ok(())
    }

    fn name(&self) -> &'static str {
        cmd::TEMPLATE
    }
}

/// List all available templates
async fn list_templates(client: &reqwest::Client, endpoint: &str) -> anyhow::Result<()> {
    let url = format!(
        "{}/api/v1/services/manifests",
        endpoint.trim_end_matches('/')
    );
    let response = client.get(&url).send().await?;

    if !response.status().is_success() {
        println!("X Failed to retrieve templates: {}", response.status());
        return Ok(());
    }

    let api_response: GardenApiResponse<Vec<TemplateInfo>> = response.json().await?;
    let templates = api_response.data;
    if templates.is_empty() {
        println!("\nNo templates available");
    } else {
        println!("\nAvailable Templates:\n");

        // Group templates by category
        let mut categories: HashMap<String, Vec<&TemplateInfo>> = HashMap::new();

        for template in &templates {
            let category = template.category.clone();
            categories.entry(category).or_default().push(template);
        }

        // Sort categories
        let mut category_names: Vec<String> = categories.keys().cloned().collect();
        category_names.sort();

        // Display templates grouped by category
        for category in category_names {
            if let Some(items) = categories.get(&category) {
                let mut sorted_items = items.clone();
                sorted_items.sort_by_key(|t| t.name.as_str());

                println!("{}:", category.to_uppercase());
                for template in sorted_items {
                    println!("  {:<18} {}", template.name, template.description);
                }
                println!();
            }
        }
    }

    Ok(())
}

/// Show details for a specific template
async fn show_template(client: &reqwest::Client, endpoint: &str, name: &str) -> anyhow::Result<()> {
    let term = ui::TerminalInfo::detect();
    let url = format!(
        "{}/api/v1/services/{}/manifest",
        endpoint.trim_end_matches('/'),
        name
    );
    let response = client.get(&url).send().await?;

    // Check status and exit early if not successful
    if !response.status().is_success() {
        eprintln!("X Template '{}' not found (HTTP {})", name, response.status());
        std::process::exit(1);
    }

    // Only proceed to parsing if status is OK
    match response.status() {
        reqwest::StatusCode::OK => {
            let body: serde_json::Value = response.json().await?;
            if let Some(content) = body.get("content").and_then(|c| c.as_str()) {
                // Parse the YAML to extract metadata
                let parsed: Result<serde_yaml::Value, _> = serde_yaml::from_str(content);

                println!("\nTemplate: {}", name);

                // Try to extract info from the service snippet
                if let Ok(yaml) = &parsed {
                    // Image/Version
                    if let Some(image) = yaml.get("image").and_then(|i| i.as_str()) {
                        println!("Image: {}", image);
                        if let Some((_, version)) = image.rsplit_once(':') {
                            println!("Version: {}", version);
                        }
                    }

                    // Container name
                    if let Some(container) = yaml.get("container_name").and_then(|c| c.as_str()) {
                        println!("Container: {}", container);
                    }

                    // Ports
                    if let Some(ports) = yaml.get("ports").and_then(|p| p.as_sequence()) {
                        if !ports.is_empty() {
                            println!("\nPorts:");
                            for port in ports {
                                if let Some(port_str) = port.as_str() {
                                    // Parse port mapping (e.g., "27017:27017")
                                    if let Some((host, container)) = port_str.split_once(':') {
                                        let host_clean = host.trim_matches('"');
                                        let container_clean = container.trim_matches('"');
                                        println!("  {} -> {}", host_clean, container_clean);
                                    } else {
                                        println!("  {}", port_str);
                                    }
                                }
                            }
                        }
                    }

                    // Environment variables
                    if let Some(env) = yaml.get("environment") {
                        let env_vars = match env {
                            serde_yaml::Value::Sequence(seq) => seq
                                .iter()
                                .filter_map(|v| v.as_str())
                                .map(|s| s.to_string())
                                .collect::<Vec<_>>(),
                            serde_yaml::Value::Mapping(map) => map
                                .iter()
                                .map(|(k, v)| {
                                    format!(
                                        "{}={}",
                                        k.as_str().unwrap_or("?"),
                                        v.as_str().unwrap_or("?")
                                    )
                                })
                                .collect::<Vec<_>>(),
                            _ => vec![],
                        };

                        if !env_vars.is_empty() {
                            println!("\nEnvironment Variables:");
                            for var in env_vars {
                                if let Some((key, value)) = var.split_once('=') {
                                    println!("  {:<30} {}", key, value);
                                } else {
                                    println!("  {}", var);
                                }
                            }
                        }
                    }

                    // Volumes
                    if let Some(volumes) = yaml.get("volumes").and_then(|v| v.as_sequence()) {
                        if !volumes.is_empty() {
                            println!("\nVolumes:");
                            for vol in volumes {
                                if let Some(vol_str) = vol.as_str() {
                                    // Parse volume mapping (e.g., "./data:/data/db" or "mongo-data:/data/db")
                                    if let Some((source, target)) = vol_str.split_once(':') {
                                        let source_clean = source.trim_matches('"');
                                        let target_clean = target.trim_matches('"');
                                        println!("  {} -> {}", source_clean, target_clean);
                                    } else {
                                        println!("  {}", vol_str);
                                    }
                                }
                            }
                        }
                    }

                    // Networks
                    if let Some(networks) = yaml.get("networks").and_then(|n| n.as_sequence()) {
                        if !networks.is_empty() {
                            println!("\nNetworks:");
                            for net in networks {
                                if let Some(net_str) = net.as_str() {
                                    println!("  {}", net_str);
                                }
                            }
                        }
                    }
                }

                // Show raw YAML content
                println!("\nDocker Compose:");
                println!("-----------------------------------------------");
                println!("{}", content);
                println!();
            } else {
                eprintln!(
                    "{}{} Invalid response format",
                    " ".repeat(ui::constants::DEFAULT_INDENT),
                    ui::status_indicator("error", term.supports_color)
                );
            }
        }
        reqwest::StatusCode::NOT_FOUND => {
            eprintln!(
                "{}{} Template '{}' not found",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("error", term.supports_color),
                name
            );
            eprintln!(
                "{}Use 'garden-rake template list' to see available templates",
                " ".repeat(ui::constants::DEFAULT_INDENT)
            );
        }
        status => {
            eprintln!(
                "{}{} Failed to retrieve template: {}",
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("error", term.supports_color),
                status
            );
        }
    }

    Ok(())
}
