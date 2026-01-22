//! Background task layer
//!
//! Long-running async tasks that run in the background:
//! - Service installation jobs (single and batch)
//! - Health monitoring loop
//! - Hardware capability detection
//! - Service discovery (Lantern registration)
//! - Network monitoring (IP change detection)
//! - Task coordination (orchestrates all background tasks)
//!
//! All tasks are non-blocking and composable.
//! Spawn with tokio::spawn() and communicate via channels/shared state.

pub mod auto_adoption;
pub mod coordinator;
pub mod discovery;
pub mod hardware_detection;
pub mod health_monitor;
pub mod job_executors;
pub mod network_monitor;

pub use auto_adoption::auto_adoption_task;
pub use coordinator::{
    start_all_background_tasks,
    start_discovery_listener, start_hardware_detection,
    start_registry_loader, start_catalog_builder,
    start_manifest_loader, start_health_monitor, start_auto_adoption,
    start_lantern_registration,
};
pub use discovery::lantern_registration_loop;
pub use hardware_detection::detect_capabilities_background;
pub use health_monitor::health_monitor_task;
pub use job_executors::{
    install_service_task, install_batch_task,
};
pub use network_monitor::{NetworkMonitor, NetworkMonitorConfig, NetworkEvent};
