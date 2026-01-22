//! Lifecycle commands
//!
//! Commands for managing service lifecycle:
//! - offer - Install/list offerings
//! - rest - Stop a service
//! - wake - Start a service
//! - remove - Remove a service (soft delete)
//! - uproot - Destroy a service completely
//! - upgrade/nourish - Update a service

pub mod rest;
pub mod wake;

pub use rest::RestCommand;
pub use wake::WakeCommand;
