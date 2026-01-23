use axum::{
    extract::{Path, Query, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::api::responses::{GardenOverview, StoneInfo, ApiResponse};
use crate::api::suggestions::{generate_suggestions, SuggestionContext};
use crate::{error_response, AppState, metrics};
use crate::domain::{placement::{PlacementRequest, PlacementResponse}, topology};
use garden_common::{api_utils::ApiErrorResponse, ChirpServiceInfo, CpuCapabilities, DetectionStatus, DiskCapabilities, HardwareCapabilities, HardwareInventory, MemoryCapabilities};
use serde::{Deserialize, Serialize};

/// GET /api/v1/garden - Get garden overview (all stones)
pub async fn get_garden_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<GardenOverview>>, (StatusCode, Json<ApiErrorResponse>)> {
    // For now, return just local stone (multi-stone discovery in future phase)
    let local_stone = get_local_stone_info(&state).await?;
    
    let overview = GardenOverview {
        stones: vec![local_stone],
        total_services: 0, // TODO: aggregate from stones
        healthy_stones: 1,
        degraded_stones: 0,
        pond_status: None, // Phase 3
    };

    let ctx = SuggestionContext::from_headers(&headers, "observe_garden");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: overview,
        suggestions,
    }))
}

/// GET /api/v1/garden/stones/:stone_name - Get specific stone details
pub async fn get_stone_v1(
    State(state): State<AppState>,
    Path(stone_name): Path<String>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<HardwareCapabilities>>, (StatusCode, Json<ApiErrorResponse>)> {
    // For now, only support local stone
    if state.stone_name != stone_name {
        return Err(error_response(
            StatusCode::NOT_FOUND,
            "STONE_NOT_FOUND",
            format!("Stone '{}' not found in garden", stone_name),
            None,
        ));
    }

    let caps = get_capabilities(&state).await;

    let ctx = SuggestionContext::from_headers(&headers, "observe_stone");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: caps,
        suggestions,
    }))
}

/// GET /api/v1/stone - Get local stone consolidated info
pub async fn get_local_stone_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<HardwareCapabilities>>, (StatusCode, Json<ApiErrorResponse>)> {
    let caps = get_capabilities(&state).await;

    let ctx = SuggestionContext::from_headers(&headers, "observe_stone");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: caps,
        suggestions,
    }))
}
/// POST /api/v1/garden/recommend - Get intelligent placement recommendation
pub async fn recommend_placement_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(request): Json<PlacementRequest>,
) -> Result<Json<ApiResponse<PlacementResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    match crate::domain::placement::recommend_placement(request.clone(), &state).await {
        Ok(response) => {
            let ctx = SuggestionContext::from_headers(&headers, "placement_success");
            let suggestions = generate_suggestions(&ctx);
            
            Ok(Json(ApiResponse {
                data: response,
                suggestions,
            }))
        }
        Err(e) => {
            tracing::error!(
                offering = %request.offering,
                error = ?e,
                "Placement recommendation failed"
            );
            
            Err(error_response(
                StatusCode::INTERNAL_SERVER_ERROR,
                "PLACEMENT_ERROR",
                format!("Failed to generate placement recommendation: {}", e),
                None,
            ))
        }
    }
}
// Helper function to build consolidated capabilities (based on main.rs capabilities handler)
async fn get_capabilities(state: &AppState) -> HardwareCapabilities {
    let (cpu_model, cpu_features, architecture) = metrics::get_cpu_info()
        .unwrap_or_else(|_| ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string()));
    
    let resources = metrics::collect_stone_resources().ok();
    let total_memory_mb = resources.as_ref()
        .map(|r| r.memory.total_bytes / 1024 / 1024)
        .unwrap_or(0);
    
    let gpus = metrics::detect_gpus();
    
    let disk = resources.as_ref().map(|r| DiskCapabilities {
        total_gb: r.disk.total_bytes / 1024 / 1024 / 1024,
        disk_type: metrics::detect_disk_type_for_mount(&r.disk.path),
    });
    
    let cores = resources.as_ref().map(|r| r.cpu.cores).unwrap_or(1);
    
    let storage = metrics::detect_storage();
    let os_version = metrics::detect_os_version();
    let kernel_version = metrics::detect_kernel_version();
    let swap_mb = metrics::detect_swap();
    
    HardwareCapabilities {
        stone_id: Some(state.stone_id.clone()),
        stone_name: state.stone_name.clone(),
        hardware: HardwareInventory {
            cpu: CpuCapabilities {
                model: if cpu_model == "Unknown" { None } else { Some(cpu_model) },
                cores,
                threads: None,
                architecture,
                features: if cpu_features.is_empty() { None } else { Some(cpu_features) },
            },
            memory: MemoryCapabilities {
                total_mb: total_memory_mb,
            },
            gpus,
            disk,
            storage,
            os_version,
            kernel_version,
            swap_mb,
            ai_capabilities: None,
        },
        runtime: None, // TODO: Add runtime info (docker version, OS, kernel)
        detection_status: DetectionStatus::Complete, // Synchronous detection
    }
}

// Helper function to get stone info summary
async fn get_local_stone_info(state: &AppState) -> Result<StoneInfo, (StatusCode, Json<ApiErrorResponse>)> {
    // Use registry instead of docker.list_services
    let registry = state.registry.read().await;
    let services_count = registry.len() as u32;
    drop(registry);

    // Get current IP from network monitor (dynamically updated)
    let current_ip = state.network_monitor.get_ip().await;
    let endpoint = format!("http://{}:{}", current_ip, state.api_port);

    Ok(StoneInfo {
        name: state.stone_name.clone(),
        endpoint,
        health: garden_common::HEALTH_HEALTHY.to_string(), // TODO: actual health check
        services_count,
        cpu_usage: 0.0, // TODO: Get from metrics
        memory_usage: 0.0, // TODO: Get from metrics
    })
}

// === TOPOLOGY API ===

/// Query parameters for topology endpoint
#[derive(Debug, Deserialize, Default)]
pub struct TopologyQueryParams {
    /// If true, include the local stone in the response
    #[serde(default)]
    pub include_local: bool,
}

/// Stone entry in topology response
#[derive(Debug, Clone, Serialize)]
pub struct TopologyStone {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: String,
    pub moss_version: String,
    pub services: Vec<ChirpServiceInfo>,
    pub last_seen: String,
}

/// Topology response containing all known stones
#[derive(Debug, Serialize)]
pub struct TopologyResponse {
    pub stones: Vec<TopologyStone>,
    pub local_stone_id: String,
    pub local_stone_name: String,
}

/// GET /api/v1/garden/topology - Get topology cache (all known stones from chirps)
///
/// Returns the list of stones discovered via UDP chirps.
/// Use ?include_local=true to also include the local stone in the response.
pub async fn get_topology_v1(
    State(state): State<AppState>,
    Query(params): Query<TopologyQueryParams>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<TopologyResponse>>, (StatusCode, Json<ApiErrorResponse>)> {
    let mut stones: Vec<TopologyStone> = topology::get_all_stones(&state.topology_cache)
        .await
        .into_iter()
        .map(|entry| TopologyStone {
            stone_id: entry.stone_id,
            stone_name: entry.stone_name,
            endpoint: entry.endpoint,
            moss_version: entry.moss_version,
            services: entry.services,
            last_seen: entry.last_seen.to_rfc3339(),
        })
        .collect();

    // Optionally include local stone
    if params.include_local {
        // Get local stone's services from registry
        let local_services: Vec<ChirpServiceInfo> = {
            let registry = state.registry.read().await;
            registry.iter().map(|svc| ChirpServiceInfo {
                name: svc.name.clone(),
                offering: svc.offering.clone(),
                category: svc.offering.clone(), // Use offering as category fallback
                status: format!("{:?}", svc.status),
            }).collect()
        };

        // Get local endpoint
        let current_ip = state.network_monitor.get_ip().await;
        let local_endpoint = format!("http://{}:{}", current_ip, state.api_port);

        stones.push(TopologyStone {
            stone_id: state.stone_id.clone(),
            stone_name: state.stone_name.clone(),
            endpoint: local_endpoint,
            moss_version: format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER")),
            services: local_services,
            last_seen: chrono::Utc::now().to_rfc3339(),
        });
    }

    let response = TopologyResponse {
        stones,
        local_stone_id: state.stone_id.clone(),
        local_stone_name: state.stone_name.clone(),
    };

    let ctx = SuggestionContext::from_headers(&headers, "topology_query");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: response,
        suggestions,
    }))
}
