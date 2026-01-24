//! Admin commands for garden-rake
//!
//! Commands for administrative stone operations:
//! - install-service / take-root: Install stone as system service
//! - rouse: Wake stone via Wake-on-LAN
//! - slumber: Shut down stone (power off)
//! - stir: Reboot stone

pub mod install_service;
pub mod stone_admin;

pub use install_service::InstallServiceCommand;
pub use stone_admin::{RouseCommand, SlumberCommand, StirCommand};
