//! Infrastructure layer - I/O operations
//!
//! This layer contains all external I/O:
//! - Container runtime (Podman/Docker)
//! - File system operations
//! - Authentication implementation (NoAuth for v0.1.0)
//! - Platform-specific utilities
//! - API response helpers
//!
//! No business logic here - pure I/O adapters.

pub mod api_helpers;
pub mod auth;
pub mod config;
pub mod container;
pub mod detection;
pub mod filesystem;
pub mod hardware;
pub mod manifest_loader;
pub mod network;
pub mod persistence;
pub mod platform;
pub mod process;
pub mod secrets;
pub mod service;

pub use api_helpers::{error_response, error_codes};
pub use auth::NoAuth;
pub use config::MossConfig;
pub use container::ContainerRuntime;
pub use network::get_local_ip;
pub use process::{kill_existing_moss_processes_graceful, check_moss_processes_exist, kill_existing_moss_processes};
#[cfg(target_os = "windows")]
pub use service::{install_windows_service, finalize_service_update, cleanup_after_service_update};
pub use filesystem::FileSystem;
pub use hardware::{detect_hardware, load_cached_capabilities, save_capabilities_cache, create_skeleton};
pub use manifest_loader::{load_offerings, default_offerings_dir};
pub use persistence::{load_registry, save_registry, save_registry_vec, load_offerings_cache, save_offerings_cache, load_or_generate_stone_id};
pub use platform::{is_running_from_removable_media, shutdown_signal};
pub use secrets::SecretsManager;
