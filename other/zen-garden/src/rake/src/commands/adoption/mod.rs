//! Adoption commands
//!
//! Commands for managing container adoption and external services:
//! - adopt - Adopt an existing container
//! - release - Release an adopted service
//! - borrow - Register an external service
//! - return - Unregister a borrowed service

pub mod adopt;
pub mod borrow;
pub mod release;
pub mod return_borrowed;

pub use adopt::AdoptCommand;
pub use borrow::BorrowCommand;
pub use release::ReleaseCommand;
pub use return_borrowed::ReturnCommand;
