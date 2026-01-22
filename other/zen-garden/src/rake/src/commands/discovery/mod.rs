//! Discovery commands
//!
//! Commands for discovering and observing stones and services:
//! - observe - Garden-wide overview
//! - watch - Real-time event streaming
//! - list - List services on a stone
//! - status - Detailed stone status
//! - adopted - List adopted services
//! - borrowed - List borrowed services

pub mod adopted;
pub mod borrowed;
pub mod list;

pub use adopted::AdoptedCommand;
pub use borrowed::BorrowedCommand;
pub use list::ListCommand;
