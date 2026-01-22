//! Zen Moss Library
//! Service discovery daemon for self-hosted infrastructure
//!
//! Architecture:
//! - domain/  - Business logic (service management, registry)
//! - infra/   - I/O operations (container runtime, filesystem, auth)
//! - api/     - HTTP handlers and routes
//! - tasks/   - Background async tasks (health monitor, job executors)
//! - bootstrap/ - Startup and initialization logic

pub mod domain;
pub mod infra;
pub mod api;
pub mod tasks;
pub mod bootstrap;
pub mod cli;

// Legacy modules (will be removed/migrated incrementally)
pub mod docker;
pub mod discovery;
pub mod mdns;
pub mod metrics;
pub mod templates;
pub mod console;
pub mod network_singletons;

// App state for HTTP handlers
pub mod app_state;

// Legacy API helpers (for backwards compatibility during migration)
pub mod api_legacy;
pub mod legacy_helpers;

// Re-export AppState and related types
pub use app_state::{
    AppState, MossEvent, Job, JobStatus,
    CompiledOffering, OfferingsFingerprint, OfferingsIndexCache,
};
pub use api_legacy::{error_response, ApiError, persist_registry_to_disk, error_codes};

// Re-export commonly used API utilities
pub use api::v1::events::emit_event;

// Re-export common types for convenience
pub use garden_common::ServiceInfo;

// Re-export domain functions and types
pub use domain::{
    adopt_offering_container, adopt_existing_containers,
    ensure_offerings_index, get_compiled_offering,
    reconcile_services,
    compatibility::CompiledCompatibility,
};

// Re-export legacy helper types and functions
pub use legacy_helpers::{
    ReconcileRequest, RefreshPayload,
    persist_registry_state, stream_logs,
    admin_shutdown, refresh_component,
};

// Re-export tasks for background execution
pub use tasks::{
    auto_adoption_task,
    install_service_task, install_batch_task,
    health_monitor_task,
    detect_capabilities_background,
    lantern_registration_loop,
    NetworkMonitor, NetworkMonitorConfig, NetworkEvent,
    // Task coordination
    start_all_background_tasks,
    start_discovery_listener, start_hardware_detection,
    start_registry_loader, start_catalog_builder,
    start_manifest_loader, start_health_monitor, start_auto_adoption,
    start_lantern_registration,
};

// Re-export bootstrap utilities
pub use bootstrap::{
    load_preinstall_manifest, PreInstallManifest,
    run_first_boot_initialization,
    router,
    bind_server, run_server, ServerConfig,
    // Startup utilities
    DockerConfig, connect_docker, init_capabilities,
    // Configuration
    DaemonConfig, init_tracing,
};

// Re-export CLI utilities
pub use cli::{Cli, Commands, parse as parse_cli, version_string};
