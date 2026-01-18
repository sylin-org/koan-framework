use serde::{Deserialize, Serialize};

/// Standard API response wrapper with optional suggestions
#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ApiResponse<T> {
    pub data: T,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub suggestions: Option<Vec<String>>,
}

impl<T> ApiResponse<T> {
    pub fn new(data: T) -> Self {
        Self {
            data,
            suggestions: None,
        }
    }

    #[allow(dead_code)]
    pub fn with_suggestions(data: T, suggestions: Vec<String>) -> Self {
        Self {
            data,
            suggestions: Some(suggestions),
        }
    }
}

/// Service creation request
#[derive(Deserialize, Debug)]
pub struct CreateServiceRequest {
    pub offering: String,
    // Future: ports and environment override fields
}
/// Service action response (rest, wake, nourish)
#[derive(Serialize, Debug)]
pub struct ServiceActionResponse {
    pub service: String,
    pub action: String,
    pub status: String,
    pub message: String,
}

/// Garden overview response
#[derive(Serialize, Debug)]
pub struct GardenOverview {
    pub stones: Vec<StoneInfo>,
    pub total_services: u32,
    pub healthy_stones: u32,
    pub degraded_stones: u32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub pond_status: Option<PondStatus>,
}

#[derive(Serialize, Debug)]
pub struct StoneInfo {
    pub name: String,
    pub endpoint: String,
    pub health: String,
    pub services_count: u32,
    pub cpu_usage: f32,
    pub memory_usage: f32,
}

#[derive(Serialize, Debug)]
pub struct PondStatus {
    pub active: bool,
    pub cornerstone: Option<String>,
    pub tier: String,
}
