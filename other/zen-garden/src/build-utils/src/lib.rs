//! # Zen Garden Build Utilities
//!
//! Shared build-time utilities for capturing build metadata.
//!
//! ## Usage in build.rs
//!
//! ```no_run
//! // build.rs
//! fn main() {
//!     garden_build_utils::capture_build_number();
//! }
//! ```
//!
//! Then in your code:
//! ```ignore
//! let build_number = env!("BUILD_NUMBER");
//! ```

/// Captures the CARGO_BUILD_NUMBER environment variable and makes it available
/// to the crate being built via BUILD_NUMBER.
///
/// This is typically called from a build.rs script. If CARGO_BUILD_NUMBER is not set,
/// it defaults to "dev".
///
/// # Example
///
/// ```no_run
/// // build.rs
/// fn main() {
///     garden_build_utils::capture_build_number();
/// }
/// ```
pub fn capture_build_number() {
    let build_number = std::env::var("CARGO_BUILD_NUMBER").unwrap_or_else(|_| "dev".to_string());
    println!("cargo:rustc-env=BUILD_NUMBER={}", build_number);
    println!("cargo:rerun-if-env-changed=CARGO_BUILD_NUMBER");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_capture_build_number() {
        // This is a build-time utility, so we just verify it compiles
        capture_build_number();
    }
}
