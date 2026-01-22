//! Management commands for garden-rake
//!
//! Commands for managing stone tending state and synchronization:
//! - tend: Manage which stone to tend to
//! - reconcile: Sync offerings with desired state
//! - pond: Manage pond security and trust network
//! - lift: Remove pond elements (zen syntax)
//! - make: Configure stone console mode (zen syntax)
//! - place: Pond placement operations (zen syntax)
//! - invite: Generate pond invitation codes (zen syntax)

pub mod invite;
pub mod lift;
pub mod make;
pub mod place;
pub mod pond;
pub mod reconcile;
pub mod tend;

pub use invite::InviteCommand;
pub use lift::{LiftCommand, LiftTarget};
pub use make::{MakeActionType, MakeCommand};
pub use place::{PlaceCommand, PlaceTarget};
pub use pond::{PondActionType, PondCommand};
pub use reconcile::ReconcileCommand;
pub use tend::TendCommand;
