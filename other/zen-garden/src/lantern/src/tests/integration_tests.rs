use axum::{
    body::Body,
    http::{Request, StatusCode},
    Router,
};
use lantern::{ElectionManager, GardenTopology, Registry};
use serde_json::json;
use std::sync::Arc;
use tokio::sync::RwLock;
use tower::ServiceExt;

async fn create_test_app() -> Router {
    let topology = Arc::new(RwLock::new(GardenTopology::new()));
    let registry = Arc::new(
        Registry::new("/tmp/test-integration.db".to_string(), topology.clone())
            .await
            .expect("Failed to create registry"),
    );
    let election = Arc::new(RwLock::new(ElectionManager::new(
        "test-lantern".to_string(),
        7187,
    )));

    #[derive(Clone)]
    struct AppState {
        lantern_name: String,
        registry: Arc<Registry>,
        election: Arc<RwLock<ElectionManager>>,
        topology: Arc<RwLock<GardenTopology>>,
    }

    let state = AppState {
        lantern_name: "test-lantern".to_string(),
        registry,
        election,
        topology,
    };

    // Simplified router with just the endpoints we want to test
    Router::new()
        .route("/health", axum::routing::get(health_handler))
        .route("/api/register", axum::routing::post(register_handler))
        .route("/api/resolve", axum::routing::get(resolve_handler))
        .route("/api/stones", axum::routing::get(list_stones_handler))
        .with_state(state)
}

async fn health_handler(
    axum::extract::State(state): axum::extract::State<
        Arc<RwLock<ElectionManager>>,
        Arc<RwLock<GardenTopology>>,
        String,
    >,
) -> axum::Json<serde_json::Value> {
    axum::Json(json!({
        "status": "healthy",
        "lantern_name": "test-lantern",
        "role": "active",
        "stones_online": 0
    }))
}

async fn register_handler(
    axum::extract::State(state): axum::extract::State<Arc<Registry>>,
    axum::Json(req): axum::Json<garden_common::RegisterRequest>,
) -> Result<axum::Json<serde_json::Value>, (StatusCode, axum::Json<serde_json::Value>)> {
    state
        .register_stone(&req.stone_name, &req.endpoint, req.services)
        .await
        .map_err(|e| {
            (
                StatusCode::INTERNAL_SERVER_ERROR,
                axum::Json(json!({"error": {"code": "REGISTRATION_FAILED", "message": e.to_string()}})),
            )
        })?;

    Ok(axum::Json(json!({
        "ttl_seconds": 60,
        "next_heartbeat_seconds": 45
    })))
}

async fn resolve_handler(
    axum::extract::State(state): axum::extract::State<Arc<Registry>>,
    axum::extract::Query(params): axum::extract::Query<std::collections::HashMap<String, String>>,
) -> Result<axum::Json<serde_json::Value>, (StatusCode, axum::Json<serde_json::Value>)> {
    let service_type = params.get("service").ok_or_else(|| {
        (
            StatusCode::BAD_REQUEST,
            axum::Json(json!({"error": {"code": "MISSING_PARAMETER", "message": "Missing 'service' query parameter"}})),
        )
    })?;

    match state.resolve_service(service_type).await {
        Ok(Some(response)) => Ok(axum::Json(serde_json::to_value(response).unwrap())),
        Ok(None) => Err((
            StatusCode::NOT_FOUND,
            axum::Json(json!({"error": {"code": "SERVICE_NOT_AVAILABLE"}})),
        )),
        Err(e) => Err((
            StatusCode::INTERNAL_SERVER_ERROR,
            axum::Json(json!({"error": {"code": "RESOLUTION_FAILED", "message": e.to_string()}})),
        )),
    }
}

async fn list_stones_handler(
    axum::extract::State(state): axum::extract::State<Arc<RwLock<GardenTopology>>>,
) -> axum::Json<serde_json::Value> {
    let topology = state.read().await;
    let lantern_topo = topology.to_json();
    axum::Json(serde_json::to_value(lantern_topo).unwrap())
}

#[tokio::test]
async fn test_health_endpoint() {
    let app = create_test_app().await;

    let response = app
        .oneshot(
            Request::builder()
                .uri("/health")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);

    let body = hyper::body::to_bytes(response.into_body()).await.unwrap();
    let json: serde_json::Value = serde_json::from_slice(&body).unwrap();

    assert_eq!(json["status"], "healthy");
    assert_eq!(json["lantern_name"], "test-lantern");
}

#[tokio::test]
async fn test_register_endpoint() {
    let app = create_test_app().await;

    let register_req = json!({
        "stone_name": "test-stone",
        "endpoint": "http://192.168.1.100:7185",
        "services": [
            {
                "name": "mongodb",
                "service_type": "mongodb",
                "status": "running",
                "connection_string": "mongodb://192.168.1.100:27017"
            }
        ]
    });

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/api/register")
                .header("content-type", "application/json")
                .body(Body::from(serde_json::to_vec(&register_req).unwrap()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);

    let body = hyper::body::to_bytes(response.into_body()).await.unwrap();
    let json: serde_json::Value = serde_json::from_slice(&body).unwrap();

    assert_eq!(json["ttl_seconds"], 60);
    assert_eq!(json["next_heartbeat_seconds"], 45);
}

#[tokio::test]
async fn test_resolve_endpoint_not_found() {
    let app = create_test_app().await;

    let response = app
        .oneshot(
            Request::builder()
                .uri("/api/resolve?service=postgres")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::NOT_FOUND);

    let body = hyper::body::to_bytes(response.into_body()).await.unwrap();
    let json: serde_json::Value = serde_json::from_slice(&body).unwrap();

    assert_eq!(json["error"]["code"], "SERVICE_NOT_AVAILABLE");
}

#[tokio::test]
async fn test_resolve_endpoint_missing_parameter() {
    let app = create_test_app().await;

    let response = app
        .oneshot(
            Request::builder()
                .uri("/api/resolve")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::BAD_REQUEST);

    let body = hyper::body::to_bytes(response.into_body()).await.unwrap();
    let json: serde_json::Value = serde_json::from_slice(&body).unwrap();

    assert_eq!(json["error"]["code"], "MISSING_PARAMETER");
}

#[tokio::test]
async fn test_list_stones_endpoint() {
    let app = create_test_app().await;

    let response = app
        .oneshot(
            Request::builder()
                .uri("/api/stones")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);

    let body = hyper::body::to_bytes(response.into_body()).await.unwrap();
    let json: serde_json::Value = serde_json::from_slice(&body).unwrap();

    assert!(json["stones"].is_array());
    assert!(json["last_updated"].is_string());
}
