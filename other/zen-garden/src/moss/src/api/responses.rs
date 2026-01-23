use serde::{Deserialize, Serialize};

// Re-export shared ApiResponse from garden-common
pub use garden_common::api_utils::ApiResponse;

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
