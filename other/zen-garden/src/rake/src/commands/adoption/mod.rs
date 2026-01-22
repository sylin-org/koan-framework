//! Adoption commands
//!
//! Commands for managing container adoption and external services:
//! - adopt - Adopt an existing container
//! - release - Release an adopted service
//! - borrow - Register an external service
//! - return - Unregister a borrowed service
//! - find strays - Find adoptable containers

pub mod adopt;
pub mod borrow;
pub mod find_strays;
pub mod release;
pub mod return_borrowed;

pub use adopt::AdoptCommand;
pub use borrow::BorrowCommand;
pub use find_strays::FindStraysCommand;
pub use release::ReleaseCommand;
pub use return_borrowed::ReturnCommand;
