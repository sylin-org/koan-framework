//! Zen Common Library
//! Shared types, networking, errors, utilities, constants, responses, and jobs for Zen Garden

pub mod types;
pub mod net;
pub mod errors;
pub mod utils;
pub mod constants;
pub mod responses;
pub mod jobs;
pub mod client;
pub mod traits;
pub mod events;
pub mod persistence;
pub mod discovery;
pub mod api_utils;
pub mod manifests;
pub mod cli_colors;

// Re-export commonly used items
pub use types::*;
pub use utils::*;
pub use responses::*;
pub use jobs::*;
pub use client::{GardenHttpClient, GardenApiResponse};
pub use cli_colors::{CliFormatter, AnsiColor, ColorSupport};

// Re-export health and vitality constants for easy access
pub use constants::{
    HEALTH_HEALTHY, HEALTH_DEGRADED, HEALTH_UNHEALTHY,
    CHECK_PASS, CHECK_WARN, CHECK_FAIL,
    COMPAT_PASS, COMPAT_FALLBACK, COMPAT_FAIL,
    VITALITY_THRIVING, VITALITY_NEEDS_ATTENTION, VITALITY_WITHERING, VITALITY_DORMANT,
    SERVICE_RUNNING, SERVICE_STOPPED, SERVICE_MAINTENANCE, SERVICE_DEGRADED, SERVICE_UNKNOWN,
    ENV_GARDEN_STONE, ENV_STONE_NAME, ENV_STONE_HOST, ENV_LANTERN_ENDPOINT,
    ENV_NO_COLOR, ENV_GARDEN_UNICODE,
    VALUE_UNKNOWN, DEFAULT_STONE_NAME,
    STATUS_COMPLETED, STATUS_SUCCESS, STATUS_FAILED, STATUS_ERROR,
    HEADER_AUTHORIZATION, AUTH_BEARER_PREFIX,
    ENDPOINT_HEALTH, ENDPOINT_CAPABILITIES,
};

// Compatibility aliases for old code (to be removed during Phase 3 refactoring)
pub mod ports {
    pub use crate::constants::{DISCOVERY_UDP, MOSS_HTTP, LANTERN_HTTP};
}

pub mod names {
    pub use crate::constants::{
        MOSS_BINARY, RAKE_BINARY, LANTERN_BINARY,
        MOSS_CONFIG, LANTERN_CONFIG,
        MOSS_SERVICE, LANTERN_SERVICE,
        CONFIG_DIR, STONE_USER, STONE_HOME, FIRST_RUN_FLAG,
    };
}

pub mod error_codes {
    pub use crate::constants::{
        INVALID_REQUEST, TEMPLATE_NOT_FOUND, CONTAINER_NOT_RUNNING, INVALID_COMPONENT,
        SERVICE_NOT_FOUND, OFFERING_NOT_FOUND, NOT_FOUND, JOB_NOT_FOUND,
        DOCKER_ERROR, INTERNAL_ERROR, REMOVE_FAILED, TEMPLATE_LOAD_FAILED, 
        UPGRADE_FAILED, INSUFFICIENT_RESOURCES,
        DOCKER_UNAVAILABLE,
        COMPATIBILITY_FAILED,
    };
}

