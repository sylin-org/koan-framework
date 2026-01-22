//! Bootstrap and initialization logic
//!
//! Handles daemon startup sequence:
//! - Preinstall manifest loading
//! - First boot initialization
//! - Auto-install requested offerings
//! - HTTP router configuration
//! - HTTP server lifecycle
//! - Docker/capabilities initialization

pub mod first_boot;
pub mod preinstall;
pub mod router;
pub mod server;
pub mod startup;

pub use first_boot::run_first_boot_initialization;
pub use preinstall::{load_preinstall_manifest, PreInstallManifest};
pub use server::{ServerConfig, bind as bind_server, run as run_server};
pub use startup::{DockerConfig, connect_docker, init_capabilities};
