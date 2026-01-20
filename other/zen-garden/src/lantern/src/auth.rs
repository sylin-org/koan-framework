use axum::{
    extract::Request,
    http::{HeaderMap, StatusCode},
    middleware::Next,
    response::Response,
};

/// JWT bearer token authentication middleware
/// Per SECURITY-SPEC.md: HMAC-SHA256 signature validation
#[allow(dead_code)]
pub struct AuthMiddleware;

impl AuthMiddleware {
    #[allow(dead_code)]
    pub async fn validate_token(
        headers: HeaderMap,
        request: Request,
        next: Next,
    ) -> Result<Response, (StatusCode, String)> {
        // Extract bearer token
        let auth_header = headers
            .get(garden_common::HEADER_AUTHORIZATION)
            .and_then(|h| h.to_str().ok())
            .ok_or((StatusCode::UNAUTHORIZED, "Missing authorization header".to_string()))?;

        if !auth_header.starts_with(garden_common::AUTH_BEARER_PREFIX) {
            return Err((
                StatusCode::UNAUTHORIZED,
                "Invalid authorization format".to_string(),
            ));
        }

        let token = &auth_header[7..];

        // TODO: Implement JWT validation per SECURITY-SPEC.md
        // - Verify HMAC-SHA256 signature with stone public key
        // - Check stone_name claim
        // - Verify expires_at (5-min TTL)
        // - Validate nonce (prevent replay)

        tracing::debug!(token = %token, "Token validation placeholder");

        Ok(next.run(request).await)
    }
}
