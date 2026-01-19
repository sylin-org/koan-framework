use axum::{
    extract::{Path, State},
    http::{HeaderMap, StatusCode},
    Json,
};
use crate::api::responses::{GardenOverview, StoneInfo, ApiResponse};
use crate::api::suggestions::{generate_suggestions, SuggestionContext};
use crate::{error_response, AppState, metrics};
use garden_common::{ApiError, CpuCapabilities, DiskCapabilities, HardwareCapabilities, HardwareInventory, MemoryCapabilities};

/// GET /api/v1/garden - Get garden overview (all stones)
pub async fn get_garden_v1(
    State(state): State<AppState>,
    headers: HeaderMap,
) -> Result<Json<ApiResponse<GardenOverview>>, (StatusCode, Json<ApiError>)> {
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
) -> Result<Json<ApiResponse<HardwareCapabilities>>, (StatusCode, Json<ApiError>)> {
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
) -> Result<Json<ApiResponse<HardwareCapabilities>>, (StatusCode, Json<ApiError>)> {
    let caps = get_capabilities(&state).await;

    let ctx = SuggestionContext::from_headers(&headers, "observe_stone");
    let suggestions = generate_suggestions(&ctx);

    Ok(Json(ApiResponse {
        data: caps,
        suggestions,
    }))
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
    
    HardwareCapabilities {
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
        },
        runtime: None, // TODO: Add runtime info (docker version, OS, kernel)
    }
}

// Helper function to get stone info summary
async fn get_local_stone_info(state: &AppState) -> Result<StoneInfo, (StatusCode, Json<ApiError>)> {
    // Use registry instead of docker.list_services
    let registry = state.registry.read().await;
    let services_count = registry.len() as u32;
    drop(registry);

    // TODO: Get actual endpoint from config
    let endpoint = "http://localhost:7185".to_string();

    Ok(StoneInfo {
        name: state.stone_name.clone(),
        endpoint,
        health: garden_common::HEALTH_HEALTHY.to_string(), // TODO: actual health check
        services_count,
        cpu_usage: 0.0, // TODO: Get from metrics
        memory_usage: 0.0, // TODO: Get from metrics
    })
}
