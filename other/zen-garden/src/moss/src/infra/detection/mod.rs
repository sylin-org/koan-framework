//! Service detection infrastructure
//!
//! Provides multiple detection methods for adopted offerings:
//! - Command execution (e.g., "mongod --version")
//! - Container inspection (Docker API)
//! - HTTP probes (health endpoints)
//!
//! Each method returns a DetectionResult with:
//! - detected: bool
//! - version: Option<String>
//! - details: String

pub mod command;
pub mod container_inspect;
pub mod http_probe;

pub use command::{detect_by_command, DetectionResult};
pub use container_inspect::detect_by_container_inspect;
pub use http_probe::detect_by_http_probe;
