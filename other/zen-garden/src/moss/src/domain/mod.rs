//! Domain layer - Business logic
//!
//! This layer contains core business logic for moss:
//! - Service lifecycle management
//! - Registry operations
//! - Template/offering management
//! - Compatibility checking
//! - Container adoption
//! - Service reconciliation
//!
//! Domain layer is pure business logic - no I/O.
//! All I/O goes through infra layer.

pub mod service_manager;
pub mod registry;
pub mod templates;
pub mod compatibility;
pub mod offerings;
pub mod health;
pub mod adoption;
pub mod reconciliation;
pub mod modes;
pub mod scoring;
pub mod metrics_collection;

pub use service_manager::ServiceManager;
pub use registry::Registry;
pub use templates::TemplateManager;
pub use compatibility::{
    CompatCheckCapabilities, CompatibilityDecision, CompiledCompatibility,
    get_current_compat_capabilities, compile_compatibility, evaluate_compatibility,
    validate_binary_architecture,
};
pub use offerings::{
    CompiledOffering, OfferingsFingerprint, OfferingsIndexCache,
    moss_version_string, current_capabilities_hash, templates_hash, rebuild_offerings_index,
    ensure_offerings_index, get_compiled_offering,
};
pub use health::{
    check_disk_health, check_memory_health,
    build_disk_component, build_memory_component,
    determine_overall_status,
};
pub use adoption::{
    adopt_offering_container, adopt_existing_containers, AdoptionResult,
};
pub use reconciliation::{
    reconcile_services, ReconciliationResult,
};
pub use modes::{
    DetectionOrchestrator, AggregatedDetectionResult,
};
