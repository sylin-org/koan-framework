//! Authentication/authorization abstraction
//!
//! **Phase 1 (v0.1.0)**: Trait slots defined, NoAuth implementation only
//! **Phase 2 (v0.2.0+)**: Add JwtAuth implementation when Pond security lands
//!
//! This design allows auth to be added without refactoring the API layer.

use async_trait::async_trait;
use std::collections::HashMap;

/// Authentication context after successful auth
#[derive(Debug, Clone)]
pub struct AuthContext {
    /// Authenticated stone name
    pub stone_name: String,

    /// Additional claims from token (empty for NoAuth)
    pub claims: HashMap<String, String>,

    /// Whether this is an admin/keystone
    pub is_admin: bool,
}

/// Authentication errors
#[derive(Debug, thiserror::Error)]
pub enum AuthError {
    #[error("Missing authorization header")]
    MissingHeader,

    #[error("Invalid authorization format")]
    InvalidFormat,

    #[error("Invalid or expired token")]
    InvalidToken,

    #[error("Insufficient permissions")]
    Forbidden,
}

/// Authentication provider trait
///
/// Implementations:
/// - NoAuth: Always succeeds, assigns default stone context
/// - JwtAuth (future): Validates JWT tokens from Pond keystone
#[async_trait]
pub trait AuthProvider: Send + Sync {
    /// Authenticate a request from Authorization header value
    ///
    /// For NoAuth: Returns default context
    /// For JwtAuth: Validates JWT, extracts claims
    async fn authenticate(&self, auth_header: Option<&str>) -> Result<AuthContext, AuthError>;

    /// Check if a context has admin permissions
    ///
    /// For NoAuth: Always false
    /// For JwtAuth: Checks keystone claim
    fn is_admin(&self, context: &AuthContext) -> bool;

    /// Get the authentication scheme name
    ///
    /// Returns: "none", "bearer", etc.
    fn scheme(&self) -> &str;
}
