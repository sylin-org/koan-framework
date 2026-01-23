//! Manifest schemas for Zen Garden offerings
//!
//! Provides structured schemas for offering manifests that support
//! multiple deployment modes (managed, adopted, borrowed).

pub mod category;
pub mod offering;

pub use category::{
    CategoryConfig,
    CategoryRegistry,
    get_category_registry,
    init_category_registry,
    load_categories,
};

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
