use anyhow::Result;
use axum::{routing::get, Router};
use clap::Parser;
use std::net::SocketAddr;
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing_subscriber::EnvFilter;

mod auth;
mod election;
mod registry;
mod state;

use election::{ElectionManager, ElectionState};
use registry::Registry;
use state::GardenTopology;

#[derive(Parser)]
#[command(name = "lantern")]
#[command(about = "Zen Garden Lantern - Service registry daemon")]
struct Cli {
    /// Lantern identifier
    #[arg(long, env = "LANTERN_NAME")]
    lantern_name: Option<String>,

    /// HTTP server port
    #[arg(long, env = "LANTERN_HTTP_PORT")]
    http_port: Option<u16>,

    /// UDP election port
    #[arg(long, env = "LANTERN_UDP_PORT")]
    udp_port: Option<u16>,

    /// Log level (trace, debug, info, warn, error)
    #[arg(long, env = "RUST_LOG")]
    log_level: Option<String>,

    /// SQLite database path
    #[arg(long, env = "LANTERN_DB_PATH")]
    db_path: Option<String>,
}

#[derive(Clone)]
struct AppState {
    lantern_name: String,
    registry: Arc<Registry>,
    election: Arc<RwLock<ElectionManager>>,
    topology: Arc<RwLock<GardenTopology>>,
}

#[tokio::main]
async fn main() -> Result<()> {
    let cli = Cli::parse();

    // Initialize logging
    let log_level = cli.log_level.unwrap_or_else(|| "info".to_string());
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(&log_level)),
        )
        .init();

    let lantern_name = cli
        .lantern_name
        .unwrap_or_else(|| "lantern-01".to_string());
    let http_port = cli.http_port.unwrap_or(7186);
    let udp_port = cli.udp_port.unwrap_or(7187);
    let db_path = cli
        .db_path
        .unwrap_or_else(|| "/var/lib/zen-garden/lantern.db".to_string());

    tracing::info!(
        lantern_name = %lantern_name,
        http_port = http_port,
        udp_port = udp_port,
        db_path = %db_path,
        "Lantern daemon starting"
    );

    // Initialize components
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Arc::new(Registry::new(db_path, topology.clone()).await?);
    let election = Arc::new(RwLock::new(ElectionManager::new(
        lantern_name.clone(),
        udp_port,
    )));

    let state = AppState {
        lantern_name,
        registry,
        election,
        topology,
    };

    // Spawn election manager
    let election_state = state.election.clone();
    let election_name = state.lantern_name.clone();
    tokio::spawn(async move {
        if let Err(e) = election::run_election_loop(election_state, election_name).await {
            tracing::error!(error = ?e, "Election loop failed");
        }
    });

    // Spawn TTL cleanup task
    let cleanup_registry = state.registry.clone();
    tokio::spawn(async move {
        registry::run_ttl_cleanup(cleanup_registry).await;
    });

    // Build HTTP server
    let app = Router::new()
        .route("/health", get(handlers::health))
        .route("/api/v1/register", axum::routing::post(handlers::register))
        .route("/api/v1/resolve", get(handlers::resolve))
        .route("/api/v1/stones", get(handlers::list_stones))
        .route("/api/v1/topology", get(handlers::get_topology))
        .route("/api/v1/events/stream", get(handlers::event_stream))
        .with_state(state);

    let addr: SocketAddr = format!("0.0.0.0:{}", http_port).parse()?;
    let listener = tokio::net::TcpListener::bind(addr).await?;

    tracing::info!(?addr, "Lantern HTTP server ready");

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await?;

    tracing::info!("Lantern daemon shutdown complete");
    Ok(())
}

async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{signal, SignalKind};

        let mut sigterm = signal(SignalKind::terminate()).expect("Failed to install SIGTERM");
        let mut sigint = signal(SignalKind::interrupt()).expect("Failed to install SIGINT");

        tokio::select! {
            _ = sigterm.recv() => tracing::info!("SIGTERM received"),
            _ = sigint.recv() => tracing::info!("SIGINT received"),
        }
    }

    #[cfg(windows)]
    {
        tokio::signal::ctrl_c()
            .await
            .expect("Failed to install Ctrl+C handler");
        tracing::info!("Ctrl+C received");
    }
}

mod handlers {
    use super::*;
    use axum::{extract::State, http::StatusCode, Json};
    use serde_json::{json, Value};

    pub async fn health(State(state): State<AppState>) -> Json<Value> {
        let election_state = state.election.read().await;
        let role = match election_state.state() {
            ElectionState::Active => "active",
            ElectionState::Dormant => "dormant",
            ElectionState::Candidate => "candidate",
        };

        let topology = state.topology.read().await;
        let stones_online = topology.stones_online_count();

        Json(json!({
            "status": "healthy",
            "lantern_name": state.lantern_name,
            "role": role,
            "stones_online": stones_online,
        }))
    }

    pub async fn register(
        State(state): State<AppState>,
        Json(req): Json<garden_common::RegisterRequest>,
    ) -> Result<Json<Value>, (StatusCode, Json<Value>)> {
        state
            .registry
            .register_stone(
                req.stone_id.as_deref(),
                &req.stone_name,
                &req.endpoint,
                req.services,
            )
            .await
            .map_err(|e| {
                (
                    StatusCode::INTERNAL_SERVER_ERROR,
                    Json(json!({"error": {"code": "REGISTRATION_FAILED", "message": e.to_string()}})),
                )
            })?;

        Ok(Json(json!({
            "ttl_seconds": 60,
            "next_heartbeat_seconds": 45
        })))
    }

    pub async fn resolve(
        State(state): State<AppState>,
        axum::extract::Query(params): axum::extract::Query<std::collections::HashMap<String, String>>,
    ) -> Result<Json<Value>, (StatusCode, Json<Value>)> {
        let service_type = params.get("service").ok_or_else(|| {
            (
                StatusCode::BAD_REQUEST,
                Json(json!({"error": {"code": "MISSING_PARAMETER", "message": "Missing 'service' query parameter"}})),
            )
        })?;

        match state.registry.resolve_service(service_type).await {
            Ok(Some(response)) => Ok(Json(serde_json::to_value(response).unwrap())),
            Ok(None) => Err((
                StatusCode::NOT_FOUND,
                Json(json!({"error": {"code": "SERVICE_NOT_AVAILABLE", "message": format!("No stone provides service type '{}'" , service_type)}})),
            )),
            Err(e) => Err((
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(json!({"error": {"code": "RESOLUTION_FAILED", "message": e.to_string()}})),
            )),
        }
    }

    pub async fn list_stones(
        State(state): State<AppState>,
    ) -> Json<Value> {
        let topology = state.topology.read().await;
        let lantern_topo = topology.to_json();
        Json(serde_json::to_value(lantern_topo).unwrap())
    }

    pub async fn get_topology(
        State(state): State<AppState>,
    ) -> Result<Json<Value>, (StatusCode, Json<Value>)> {
        let election_state = state.election.read().await;

        match election_state.state() {
            ElectionState::Active => {
                let topology = state.topology.read().await;
                let lantern_topo = topology.to_json();
                Ok(Json(serde_json::to_value(lantern_topo).unwrap()))
            }
            _ => Err((
                StatusCode::SERVICE_UNAVAILABLE,
                Json(json!({
                    "error": "Not primary",
                    "primary_endpoint": election_state.active_endpoint()
                })),
            )),
        }
    }

    pub async fn event_stream(
        State(_state): State<AppState>,
    ) -> (StatusCode, String) {
        // TODO: Implement SSE
        (StatusCode::NOT_IMPLEMENTED, "SSE not yet implemented".to_string())
    }
}
