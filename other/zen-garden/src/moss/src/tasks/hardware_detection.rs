//! Hardware capability detection background task
//!
//! Progressive detection strategy:
//! 1. Fast Phase: CPU, memory, disk (~100ms) - results available immediately
//! 2. Slow Phase: GPU detection (2-6 seconds on Windows) - runs in background
//! 3. Final Phase: Storage, OS, kernel, swap - completes detection
//!
//! This non-blocking approach allows the daemon to start serving requests
//! while GPU detection completes in the background.

use crate::AppState;
use crate::domain::ensure_offerings_index;
use crate::{console, metrics};
use crate::infra::save_capabilities_cache;
use garden_common::{
    AiCapabilitiesSummary, CpuCapabilities, DetectionStatus, DiskCapabilities,
    GpuInfo, HardwareCapabilities, HardwareInventory, MemoryCapabilities, RuntimeInfo,
};
use std::collections::HashSet;
use std::sync::Arc;
use tokio::sync::RwLock;

/// Background hardware capability detection with progressive results
///
/// This task should be spawned with tokio::spawn() at daemon startup.
/// It runs once, populating the capabilities cache in multiple phases:
///
/// # Progressive Detection Strategy
/// 1. **Phase 1: CPU (Fast, ~100ms)**
///    - Detects CPU model, cores, features, architecture
///    - Updates cache immediately with partial results
///    - Daemon can start serving requests
///
/// 2. **Phase 2: GPU (Slow, 2-6 seconds on Windows)**
///    - Runs DXDiag or equivalent GPU detection
///    - May take several seconds, especially on Windows
///    - Updates cache with complete GPU data
///
/// 3. **Phase 3: System Info**
///    - Storage, OS version, kernel, swap
///    - Completes detection
///    - Persists final results to disk
///
/// 4. **Post-Detection: Offerings Re-evaluation**
///    - Rebuilds offerings index with detected hardware
///    - Updates compatibility warnings (no GPU → no Ollama, etc.)
///
/// # Non-Blocking
/// This function is designed to run in the background. The daemon doesn't
/// wait for it to complete before starting the HTTP server.
///
/// # Parameters
/// - `stone_name`: Stone identifier
/// - `caps_arc`: Shared capabilities cache (updated progressively)
/// - `console`: Console printer for status updates
/// - `state`: Application state (for offerings re-evaluation)
///
/// # Example
/// ```rust,ignore
/// let stone_name = "stone-01".to_string();
/// let caps_arc = Arc::new(RwLock::new(None));
/// let console = Arc::new(ConsolePrinter::new());
/// let state_clone = state.clone();
///
/// tokio::spawn(async move {
///     detect_capabilities_background(
///         stone_name,
///         caps_arc,
///         console,
///         state_clone,
///     ).await;
/// });
/// // Daemon continues, capabilities populated progressively
/// ```
///
/// Build AI capabilities summary from GPU list
fn build_ai_capabilities_summary(gpus: &[GpuInfo], detection_complete: bool) -> AiCapabilitiesSummary {
    let mut runtimes: HashSet<String> = HashSet::new();
    let mut vendors: HashSet<String> = HashSet::new();
    let mut total_vram_mb: u64 = 0;
    let mut gpu_count: usize = 0;

    for gpu in gpus {
        // Collect unique runtimes (both simple and versioned formats)
        for runtime in &gpu.ai_runtimes {
            runtimes.insert(runtime.clone());
        }

        // Collect unique vendors (lowercase for consistency)
        vendors.insert(gpu.vendor.to_lowercase());

        // Sum VRAM
        if let Some(vram) = gpu.vram_mb {
            total_vram_mb += vram;
            gpu_count += 1;
        }
    }

    // Convert HashSets to sorted Vecs for consistent output
    let mut runtimes_vec: Vec<String> = runtimes.into_iter().collect();
    runtimes_vec.sort();

    let mut vendors_vec: Vec<String> = vendors.into_iter().collect();
    vendors_vec.sort();

    AiCapabilitiesSummary {
        runtimes: runtimes_vec,
        vendors: vendors_vec,
        total_vram_mb,
        gpu_count,
        detection_complete,
    }
}

pub async fn detect_capabilities_background(
    stone_name: String,
    caps_arc: Arc<RwLock<Option<HardwareCapabilities>>>,
    console: Arc<console::ConsolePrinter>,
    state: AppState,
) {
    tracing::info!("Starting background hardware capability detection...");

    // === PHASE 1: CPU Detection (fast, <100ms) ===
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        "[CAPABILITY DETECTION] Detecting CPU features".to_string()
    ));

    let (cpu_model, cpu_features, architecture) = match metrics::get_cpu_info() {
        Ok(result) => result,
        Err(e) => {
            tracing::error!(error = ?e, "Failed to get CPU info");
            ("Unknown".to_string(), vec![], std::env::consts::ARCH.to_string())
        }
    };

    let resources = metrics::collect_stone_resources().ok();
    let cpu_cores = resources.as_ref().map(|r| r.cpu.cores).unwrap_or(1);
    let total_memory_mb = resources.as_ref()
        .map(|r| r.memory.total_bytes / 1024 / 1024)
        .unwrap_or(0);

    let disk = resources.as_ref().map(|r| DiskCapabilities {
        total_gb: r.disk.total_bytes / 1024 / 1024 / 1024,
        disk_type: metrics::detect_disk_type_for_mount(&r.disk.path),
    });

    tracing::info!("CPU detection complete: {} cores", cpu_cores);
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        format!("[CAPABILITY DETECTION] CPU: {} cores, {} features", cpu_cores, cpu_features.len())
    ));

    // Update CPU data incrementally (preserve existing data, update CPU fields)
    let updated_caps = {
        let mut guard = caps_arc.write().await;
        let mut caps = guard.take().unwrap_or_else(|| HardwareCapabilities {
            stone_id: None, // Will be set from AppState
            stone_name: stone_name.clone(),
            hardware: HardwareInventory {
                cpu: CpuCapabilities {
                    model: None,
                    cores: 0,
                    threads: None,
                    architecture: std::env::consts::ARCH.to_string(),
                    features: None,
                },
                memory: MemoryCapabilities { total_mb: 0 },
                gpus: vec![],
                disk: None,
                storage: vec![],
                os_version: None,
                kernel_version: None,
                swap_mb: None,
                ai_capabilities: None,
            },
            runtime: Some(RuntimeInfo {
                docker_version: None,
                os: std::env::consts::OS.to_string(),
                kernel: None,
            }),
            detection_status: DetectionStatus::Scanning,
        });

        // Update CPU fields only
        caps.hardware.cpu = CpuCapabilities {
            model: if cpu_model == "Unknown" { None } else { Some(cpu_model.clone()) },
            cores: cpu_cores,
            threads: None,
            architecture: architecture.clone(),
            features: if cpu_features.is_empty() { None } else { Some(cpu_features.clone()) },
        };
        caps.hardware.memory = MemoryCapabilities { total_mb: total_memory_mb };
        caps.hardware.disk = disk.clone();

        // Upgrade status if needed (Scanning → Partial, but preserve Complete)
        if caps.detection_status == DetectionStatus::Scanning {
            caps.detection_status = DetectionStatus::Partial;
        }

        let cloned = caps.clone();
        *guard = Some(caps);
        cloned
    };

    // Persist updated CPU data to disk (non-blocking for consumers)
    if let Err(e) = save_capabilities_cache(&updated_caps).await {
        tracing::warn!(error = ?e, "Failed to save updated capabilities after CPU detection");
    }
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::System,
        console::EventStatus::Updated,
        "Hardware capabilities (CPU ready)".to_string()
    ));

    // === PHASE 2: GPU Detection (slow, 2-6 seconds on Windows) ===
    tracing::info!("Starting GPU detection (may take 2-6 seconds on Windows)...");
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Active,
        "[CAPABILITY DETECTION] Detecting GPUs (DXDiag, 2-6 sec)".to_string()
    ));

    let gpus = metrics::detect_gpus();
    let gpu_count = gpus.len();
    tracing::info!(gpu_count = gpus.len(), "GPU detection complete");
    console.emit(console::ConsoleEvent::new(
        console::EventCategory::Ops,
        console::EventStatus::Completed,
        format!("[CAPABILITY DETECTION] Found {} GPU(s)", gpu_count)
    ));

    // === PHASE 3: Storage, OS, Kernel, Swap Detection ===
    tracing::info!("Detecting storage and system information...");
    let storage = metrics::detect_storage();
    let os_version = metrics::detect_os_version();
    let kernel_version = metrics::detect_kernel_version();
    let swap_mb = metrics::detect_swap();
    tracing::info!("System information detection complete");

    // Update complete capabilities incrementally (update GPU + storage + system info fields)
    let complete_caps = {
        let mut guard = caps_arc.write().await;
        let mut caps = guard.take().expect("capabilities should exist after CPU phase");

        // Update GPU fields
        caps.hardware.gpus = gpus.clone();

        // Build and update AI capabilities summary
        caps.hardware.ai_capabilities = Some(build_ai_capabilities_summary(
            &gpus,
            true  // detection_complete = true
        ));

        // Update storage and system info fields
        caps.hardware.storage = storage;
        caps.hardware.os_version = os_version;
        caps.hardware.kernel_version = kernel_version;
        caps.hardware.swap_mb = swap_mb;

        // Mark detection as complete
        caps.detection_status = DetectionStatus::Complete;

        let cloned = caps.clone();
        *guard = Some(caps);
        cloned
    };

    // Persist complete data to disk
    match save_capabilities_cache(&complete_caps).await {
        Ok(_) => {
            tracing::info!("Complete capabilities saved to disk");
            console.emit(console::ConsoleEvent::new(
                console::EventCategory::Ops,
                console::EventStatus::Completed,
                "[CAPABILITY DETECTION] Cache persisted to disk".to_string()
            ));
        },
        Err(e) => tracing::warn!(error = ?e, "Failed to save complete capabilities"),
    }

    tracing::info!("Hardware capability detection complete");

    // Re-evaluate offerings index now that complete hardware is known
    // This ensures compatibility warnings update (e.g., no AI → no Ollama, no AVX → MongoDB warning)
    tracing::info!("Re-evaluating offerings compatibility with detected hardware...");
    if let Err(e) = ensure_offerings_index(&state, true).await {
        tracing::warn!(error = ?e, "Failed to rebuild offerings index after detection");
    } else {
        console.emit(console::ConsoleEvent::new(
            console::EventCategory::Ops,
            console::EventStatus::Completed,
            "[OFFERINGS] Compatibility re-evaluated".to_string()
        ));
    }
}
