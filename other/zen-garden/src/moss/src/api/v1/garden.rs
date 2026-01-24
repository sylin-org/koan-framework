use axum::{
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::api::responses::{GardenOverview, StoneInfo, ApiResponse};
use crate::api::suggestions::{generate_suggestions, SuggestionContext};
use crate::{error_response, AppState, metrics};
use crate::domain::{placement::{PlacementRequest, PlacementResponse}, topology};
use garden_common::{api_utils::ApiErrorResponse, CpuCapabilities, DetectionStatus, DiskCapabilities, HardwareCapabilities, HardwareInventory, MemoryCapabilities};

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

/// GET /api/v1/garden/topology - Get all known stones in the garden
///
/// Returns all stones as TopologyEntry objects: self entry first, then peers from cache.
/// No conversion needed - TopologyEntry is the universal model.
pub async fn get_topology_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<Vec<topology::TopologyEntry>>>, (StatusCode, Json<ApiErrorResponse>)> {
    // Step 1: Read self entry (single source of truth for local stone)
    let self_entry = state.self_entry.read().await.clone();
    
    tracing::debug!(
        stone_id = %self_entry.stone_id,
        stone_name = %self_entry.stone_name,
        services = self_entry.services.len(),
        health = %self_entry.health,
        "Topology: self entry prepared"
    );

    // Step 2: Start response with self entry first
    let mut stones = vec![self_entry.clone()];

    // Step 3: Add all cached peer stones (skipping self if present)
    let cache_entries = topology::get_all_stones(&state.topology_cache).await;

    for entry in cache_entries {
        if entry.stone_id == state.stone_id {
            tracing::debug!(
                cached_stone_id = %entry.stone_id,
                "Topology: skipping self from cache"
            );
            continue;
        }

        stones.push(entry);
    }

    tracing::debug!(
        total_stones = stones.len(),
        "Topology: response built"
    );

    let ctx = SuggestionContext::from_headers(&headers, "topology_query");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: stones,
        suggestions,
    }))
}
