//! Bootstrap and initialization logic
//!
//! Handles daemon startup sequence:
//! - Preinstall manifest loading
//! - First boot initialization
//! - Auto-install requested offerings

pub mod first_boot;
pub mod preinstall;

pub use first_boot::run_first_boot_initialization;
pub use preinstall::{load_preinstall_manifest, PreInstallManifest};
