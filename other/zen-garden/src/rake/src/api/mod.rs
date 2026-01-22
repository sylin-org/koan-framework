//! API client utilities
//!
//! Provides typed response parsing and HTTP helpers to eliminate
//! repetitive JSON extraction patterns in command handlers.

pub mod responses;

pub use responses::{
    extract_data, extract_array, extract_string, extract_bool,
    extract_services, ApiResult,
};
