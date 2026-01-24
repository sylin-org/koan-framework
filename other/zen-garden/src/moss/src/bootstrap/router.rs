//! HTTP router configuration
//!
//! Defines all API routes for the Moss HTTP server.
//! Extracted from main.rs for cleaner separation of concerns.

use axum::{
    routing::{get, post, delete},
    Router,
};
use tower_http::trace::TraceLayer;
use crate::{api, AppState};

/// Configure the HTTP router with all API endpoints
///
/// Routes are organized by category:
/// - Root level: health, capabilities, metrics
/// - /api/v1/offerings: Offering management (human layer)
/// - /api/v1/services: Service management (technical layer)
/// - /api/v1/stone: Stone software operations (upgrade, deploy)
/// - /api/v1/events, /api/v1/jobs: Events and job tracking
/// - /api/v1/garden: Garden topology
/// - /api/v1/pond: Security/trust management
/// - /api/v1/console: Console control
/// - /api/v1/admin/moss: Moss daemon admin (shutdown, take-root)
/// - /api/v1/admin/stone: Stone machine admin (shutdown, reboot, wake)
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
        // Note: /api/v1/services is a unified endpoint:
        //   - No params: lists all local services
        //   - With ?q=, ?name=, etc.: searches/filters across garden
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

        // V1 API - Stone operations (software)
        .route("/api/v1/stone/info", get(api::v1::stone::get_stone_info_v1))
        .route("/api/v1/stone/upgrade", post(api::v1::stone::upgrade_stone_v1))
        .route("/api/v1/stone/deploy", post(api::v1::stone::deploy_stone_v1))

        // V1 API - Events & Jobs
        .route("/api/v1/events", get(api::v1::events::stream_events))
        .route("/api/v1/jobs", get(api::v1::jobs::list_jobs))
        .route("/api/v1/jobs/:job_id", get(api::v1::jobs::get_job_status))

        // V1 API - Garden topology
        .route("/api/v1/garden", get(api::v1::garden::get_garden_v1))
        .route("/api/v1/garden/topology", get(api::v1::garden::get_topology_v1))
        .route("/api/v1/garden/recommend", post(api::v1::garden::recommend_placement_v1))
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

        // V1 API - Admin operations (privileged)
        // See: docs/decisions/API-0002-admin-hierarchy.md
        .route("/api/v1/admin/moss/shutdown", post(api::v1::admin::moss_shutdown))
        .route("/api/v1/admin/moss/take-root", post(api::v1::admin::moss_take_root))
        .route("/api/v1/admin/stone/shutdown", post(api::v1::admin::stone_shutdown))
        .route("/api/v1/admin/stone/reboot", post(api::v1::admin::stone_reboot))
        .route("/api/v1/admin/stone/:name/wake", post(api::v1::admin::stone_wake))

        // Apply 200 MB body limit to all routes
        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024))

        // Request tracing layer - logs method, uri, and status
        .layer(TraceLayer::new_for_http())

        .with_state(state)
}
