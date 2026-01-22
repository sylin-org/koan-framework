//! Lifecycle commands
//!
//! Commands for managing service lifecycle:
//! - offer - Install/list offerings
//! - rest - Stop a service
//! - wake - Start a service
//! - remove - Remove a service (soft delete)
//! - uproot - Destroy a service completely
//! - upgrade/nourish - Update a service

pub mod remove;
pub mod rest;
pub mod upgrade;
pub mod uproot;
pub mod wake;

pub use remove::RemoveCommand;
pub use rest::RestCommand;
pub use upgrade::UpgradeCommand;
pub use uproot::UprootCommand;
pub use wake::WakeCommand;
