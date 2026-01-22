//! Admin commands for garden-rake
//!
//! Commands for administrative stone operations:
//! - install-service / take-root: Install stone as system service

pub mod install_service;

pub use install_service::InstallServiceCommand;
