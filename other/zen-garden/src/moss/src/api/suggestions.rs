use axum::http::HeaderMap;

/// Context for generating context-aware suggestions
pub struct SuggestionContext {
    pub operation: String,
    pub service_name: Option<String>,
    pub success: bool,
    pub quiet_mode: bool,
}

impl SuggestionContext {
    pub fn from_headers(headers: &HeaderMap, operation: &str) -> Self {
        let quiet_mode = headers
            .get("X-Quiet")
            .and_then(|v| v.to_str().ok())
            .map(|v| v == "true")
            .unwrap_or(false);

        Self {
            operation: operation.to_string(),
            service_name: None,
            success: true,
            quiet_mode,
        }
    }

    #[allow(dead_code)]
    pub fn with_service(mut self, service: String) -> Self {
        self.service_name = Some(service);
        self
    }

    #[allow(dead_code)]
    pub fn with_success(mut self, success: bool) -> Self {
        self.success = success;
        self
    }
}

/// Generate context-aware suggestions based on operation
pub fn generate_suggestions(ctx: &SuggestionContext) -> Option<Vec<String>> {
    if ctx.quiet_mode {
        return None;
    }

    let suggestions = match ctx.operation.as_str() {
        "create_service" if ctx.success => vec![
            "garden-rake list                 View installed services".to_string(),
            "garden-rake watch <service>      Stream service logs".to_string(),
            "garden-rake observe              View garden status".to_string(),
        ],
        "rest_service" if ctx.success => vec![
            format!("garden-rake wake {}          Restart service", ctx.service_name.as_deref().unwrap_or("<service>")),
            "garden-rake observe              View all services".to_string(),
        ],
        "wake_service" if ctx.success => vec![
            format!("garden-rake watch {}         Monitor service", ctx.service_name.as_deref().unwrap_or("<service>")),
            "garden-rake observe              View status".to_string(),
        ],
        "nourish_service" if ctx.success => vec![
            format!("garden-rake observe {}       Verify upgrade", ctx.service_name.as_deref().unwrap_or("<service>")),
            "garden-rake watch                Stream events".to_string(),
        ],
        "delete_service" if ctx.success => vec![
            "garden-rake observe              View remaining services".to_string(),
            "garden-rake explore              Browse available offerings".to_string(),
        ],
        "list_services" => vec![
            "garden-rake explore              Browse offerings catalog".to_string(),
            "garden-rake observe all          View all stones".to_string(),
        ],
        "get_service" => vec![
            format!("garden-rake watch {}         Stream logs", ctx.service_name.as_deref().unwrap_or("<service>")),
            format!("garden-rake rest {}          Stop service", ctx.service_name.as_deref().unwrap_or("<service>")),
            format!("garden-rake nourish {}       Upgrade service", ctx.service_name.as_deref().unwrap_or("<service>")),
        ],
        "observe_garden" => vec![
            "garden-rake tend stone-<name>    Focus on specific stone".to_string(),
            "garden-rake watch                Stream garden events".to_string(),
            "garden-rake explore              Browse offerings".to_string(),
        ],
        "observe_stone" => vec![
            "garden-rake watch                Stream this stone's events".to_string(),
            "garden-rake touch stone-<name>   Deep diagnostics".to_string(),
            "garden-rake observe all          View all stones".to_string(),
        ],
        _ => vec![],
    };

    if suggestions.is_empty() {
        None
    } else {
        Some(suggestions.into_iter().take(4).collect())
    }
}
