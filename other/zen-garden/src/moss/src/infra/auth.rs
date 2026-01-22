//! Authentication implementation
//!
//! Phase 1 (v0.1.0): NoAuth - always succeeds
//! Phase 2 (v0.2.0+): JWT validation with Pond keystone

use garden_common::traits::auth::{AuthProvider, AuthContext, AuthError};
use async_trait::async_trait;

/// No authentication - always succeeds
///
/// Returns default context with stone name from environment or "default-stone".
/// Used in v0.1.0 before Pond security is implemented.
pub struct NoAuth {
    default_stone: String,
}

impl NoAuth {
    /// Create a new NoAuth provider
    pub fn new(default_stone: impl Into<String>) -> Self {
        Self {
            default_stone: default_stone.into(),
        }
    }
}

#[async_trait]
impl AuthProvider for NoAuth {
    async fn authenticate(&self, _auth_header: Option<&str>) -> Result<AuthContext, AuthError> {
        // Always succeed with default context
        Ok(AuthContext {
            stone_name: self.default_stone.clone(),
            claims: std::collections::HashMap::new(),
            is_admin: false,
        })
    }

    fn is_admin(&self, _context: &AuthContext) -> bool {
        // No admin concept in NoAuth
        false
    }

    fn scheme(&self) -> &str {
        "none"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_no_auth_always_succeeds() {
        let auth = NoAuth::new("test-stone");

        let ctx = auth.authenticate(None).await.unwrap();
        assert_eq!(ctx.stone_name, "test-stone");
        assert!(!ctx.is_admin);
        assert!(ctx.claims.is_empty());
    }

    #[tokio::test]
    async fn test_no_auth_ignores_header() {
        let auth = NoAuth::new("test-stone");

        let ctx = auth.authenticate(Some("Bearer fake-token")).await.unwrap();
        assert_eq!(ctx.stone_name, "test-stone");
    }

    #[test]
    fn test_no_auth_scheme() {
        let auth = NoAuth::new("test-stone");
        assert_eq!(auth.scheme(), "none");
    }

    #[test]
    fn test_no_admin() {
        let auth = NoAuth::new("test-stone");
        let ctx = AuthContext {
            stone_name: "test".into(),
            claims: std::collections::HashMap::new(),
            is_admin: false,
        };

        assert!(!auth.is_admin(&ctx));
    }
}
