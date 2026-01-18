//! Zen Common Library
//! Shared types, networking, errors, utilities, constants, responses, and jobs for Zen Garden

pub mod types;
pub mod net;
pub mod errors;
pub mod utils;
pub mod constants;
pub mod responses;
pub mod jobs;

// Re-export commonly used items
pub use types::*;
pub use utils::*;
pub use responses::*;
pub use jobs::*;

// Compatibility aliases for old code (to be removed during Phase 3 refactoring)
pub mod ports {
    pub use crate::constants::{DISCOVERY_UDP, MOSS_HTTP, LANTERN_HTTP};
}

pub mod names {
    pub use crate::constants::{
        MOSS_BINARY, RAKE_BINARY, LANTERN_BINARY,
        MOSS_CONFIG, LANTERN_CONFIG,
        MOSS_SERVICE, LANTERN_SERVICE,
        CONFIG_DIR, STONE_USER, STONE_HOME, FIRST_RUN_FLAG, MOSS_REGISTRY, MOSS_OFFERINGS_INDEX,
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

