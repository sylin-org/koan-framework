//! Manifest schemas for Zen Garden offerings
//!
//! Provides structured schemas for offering manifests that support
//! multiple deployment modes (managed, adopted, borrowed).

pub mod offering;

pub use offering::{
    OfferingManifest,
    DetectionRule,
    DetectionMethod,
    DetectionConfig,
    CommandDetection,
    ContainerInspectDetection,
    HttpProbeDetection,
    ControlConfig,
    LocationConfig,
    HealthConfig,
};
