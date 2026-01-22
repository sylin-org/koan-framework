//! HTTP router configuration
//!
//! Defines all API routes for the Moss HTTP server.
//! Extracted from main.rs for cleaner separation of concerns.

use axum::{
    routing::{get, post, delete},
    Router,
};
use crate::{api, AppState};

/// Configure the HTTP router with all API endpoints
///
/// Routes are organized by category:
/// - Root level: health, capabilities, metrics
/// - /api/v1/offerings: Offering management (human layer)
/// - /api/v1/services: Service management (technical layer)
/// - /api/v1/stone: Stone operations
/// - /api/v1/events, /api/v1/jobs: Events and job tracking
/// - /api/v1/garden: Garden topology
/// - /api/v1/pond: Security/trust management
/// - /api/v1/console: Console control
/// - /admin: Administrative endpoints
pub fn configure(state: AppState) -> Router {
    Router::new()
        // Standard health/monitoring endpoints (root level)
        .route("/health", get(api::v1::health::get_health))
        .route("/capabilities", get(api::v1::capabilities::get_capabilities))
        .route("/metrics", get(api::v1::metrics::get_metrics))

        // V1 API - Offerings (Human Layer)
        .route("/api/v1/offerings", get(api::v1::offerings::list_offerings_v1))
        .route("/api/v1/offerings", post(api::v1::offerings::plant_offering_v1))
        .route("/api/v1/offerings/:name", get(api::v1::offerings::get_offering_v1))
        .route("/api/v1/offerings/:name", delete(api::v1::offerings::take_away_offering_v1))
        .route("/api/v1/offerings/:name/manifest", get(api::v1::offerings::get_offering_manifest_v1))
        .route("/api/v1/offerings/heal", post(api::v1::offerings::heal_garden_v1))
        .route("/api/v1/offerings/refresh", post(api::v1::offerings::refresh_catalog_v1))

        // V1 API - Adoption (Multi-mode offerings)
        .route("/api/v1/offerings/adoptable", get(api::v1::adoption::list_adoptable_v1))
        .route("/api/v1/offerings/adopted", get(api::v1::adoption::list_adopted_v1))
        .route("/api/v1/offerings/borrowed", get(api::v1::adoption::list_borrowed_v1))
        .route("/api/v1/offerings/:offering/adopt", post(api::v1::adoption::adopt_offering_v1))
        .route("/api/v1/offerings/:offering/adopt", delete(api::v1::adoption::unadopt_offering_v1))
        .route("/api/v1/adoption/borrow", post(api::v1::adoption::borrow_service_v1))
        .route("/api/v1/adoption/borrow/:name", delete(api::v1::adoption::unborrow_service_v1))

        // V1 API - Services (Technical Layer)
        .route("/api/v1/services/manifests", get(api::v1::services::list_manifests_v1))
        .route("/api/v1/services/:name/manifest", get(api::v1::services::get_manifest_v1))
        .route("/api/v1/services", get(api::v1::services::list_services_v1))
        .route("/api/v1/services", post(api::v1::services::create_service_v1))
        .route("/api/v1/services/:service", get(api::v1::services::get_service_v1))
        .route("/api/v1/services/:service", delete(api::v1::services::delete_service_v1))
        .route("/api/v1/services/:service/logs", get(api::v1::services::stream_service_logs_v1))
        .route("/api/v1/services/:service/restart", post(api::v1::services::restart_service_v1))
        .route("/api/v1/services/:service/rest", post(api::v1::services::rest_service_v1))
        .route("/api/v1/services/:service/wake", post(api::v1::services::wake_service_v1))
        .route("/api/v1/services/:service/nourish", post(api::v1::services::nourish_service_v1))
        .route("/api/v1/services/:service/destroy", post(api::v1::services::destroy_service_v1))
        .route("/api/v1/services/:service/cordon", post(api::v1::services::cordon_service_v1))
        .route("/api/v1/services/reconcile", post(api::v1::services::reconcile_inventory_v1))
        .route("/api/v1/services/refresh", post(api::v1::services::refresh_manifests_v1))

        // V1 API - Stone operations
        .route("/api/v1/stone/upgrade", post(api::v1::stone::upgrade_stone_v1))
        .route("/api/v1/stone/shutdown", post(api::v1::stone::shutdown_stone_v1))

        // V1 API - Events & Jobs
        .route("/api/v1/events", get(api::v1::events::stream_events))
        .route("/api/v1/jobs", get(api::v1::jobs::list_jobs))
        .route("/api/v1/jobs/:job_id", get(api::v1::jobs::get_job_status))

        // V1 API - Garden topology
        .route("/api/v1/garden", get(api::v1::garden::get_garden_v1))
        .route("/api/v1/garden/stones/:stone_name", get(api::v1::garden::get_stone_v1))
        .route("/api/v1/stone", get(api::v1::garden::get_local_stone_v1))

        // V1 API - Pond security
        .route("/api/v1/pond/init", post(api::v1::pond::pond_init_v1))
        .route("/api/v1/pond", delete(api::v1::pond::pond_remove_v1))
        .route("/api/v1/pond/invite", post(api::v1::pond::pond_invite_v1))
        .route("/api/v1/pond/join", post(api::v1::pond::pond_join_v1))
        .route("/api/v1/pond/stones/:stone_name", delete(api::v1::pond::pond_untrust_v1))
        .route("/api/v1/pond/status", get(api::v1::pond::pond_status_v1))

        // V1 API - Console control
        .route("/api/v1/console/mode", get(api::v1::console::get_console_mode_v1))
        .route("/api/v1/console/mode", post(api::v1::console::set_console_mode_v1))

        // Admin endpoints
        .route("/admin/take-root", post(api::v1::admin::admin_take_root))

        // Apply 200 MB body limit to all routes
        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024))

        .with_state(state)
}
