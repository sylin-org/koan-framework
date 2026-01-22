//! Offering modes domain logic
//!
//! Business logic for multi-mode offerings:
//! - Detection orchestration with caching
//! - Adoption workflows
//! - Borrowed service management

pub mod detection;

pub use detection::{DetectionOrchestrator, AggregatedDetectionResult};
