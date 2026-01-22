//! Local commands for garden-rake
//!
//! Commands that don't require a stone endpoint:
//! - ceremony: Guided workflow placeholders
//! - browse: Browse command manifest
//! - template: Browse service templates

pub mod browse;
pub mod ceremony;
pub mod template;

pub use browse::BrowseCommand;
pub use ceremony::CeremonyCommand;
pub use template::{TemplateAction, TemplateCommand};
