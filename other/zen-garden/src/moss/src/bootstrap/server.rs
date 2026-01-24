//! HTTP server lifecycle management
//!
//! Handles server binding, graceful shutdown, and error handling.
//! Extracted from main.rs for cleaner separation of concerns.

use std::future::Future;
use std::net::SocketAddr;
use std::pin::Pin;
use std::sync::Arc;
use axum::Router;
use tokio::net::TcpListener;
use crate::console::{ConsolePrinter, ConsoleEvent, EventCategory, EventStatus};
use crate::infra::platform::shutdown_signal;

/// Server configuration
pub struct ServerConfig {
    pub port: u16,
    pub graceful_shutdown_timeout_secs: u64,
}

impl Default for ServerConfig {
    fn default() -> Self {
        Self {
            port: garden_common::ports::MOSS_HTTP,
            graceful_shutdown_timeout_secs: 5,
        }
    }
}

/// Bind to the specified address with user-friendly error messages
pub async fn bind(port: u16, console: &ConsolePrinter) -> anyhow::Result<TcpListener> {
    let addr: SocketAddr = format!("0.0.0.0:{}", port).parse()?;

    match TcpListener::bind(addr).await {
        Ok(listener) => Ok(listener),
        Err(e) => {
            let error_msg = if e.kind() == std::io::ErrorKind::AddrInUse {
                format!(
                    "Port {} is already in use. Another garden-moss instance may be running.\n\
                    Try: Stop-Process -Name garden-moss -Force\n\
                    Or use a different port: garden-moss --port <port>",
                    port
                )
            } else {
                format!(
                    "Failed to bind HTTP server to {}:{}: {}\n\
                    Check firewall settings and ensure the port is available.",
                    addr.ip(), addr.port(), e
                )
            };

            console.emit(ConsoleEvent::new(
                EventCategory::System,
                EventStatus::Failed,
                error_msg.clone()
            ));

            anyhow::bail!(error_msg);
        }
    }
}

/// Shutdown callback type for goodbye announcements
pub type ShutdownCallback = Box<dyn FnOnce() -> Pin<Box<dyn Future<Output = ()> + Send>> + Send>;

/// Run the HTTP server with graceful shutdown support
///
/// This function handles:
/// - Server startup logging
/// - Graceful shutdown on SIGTERM/SIGINT/Ctrl+C
/// - Admin-initiated shutdown via notify channel
/// - In-flight request draining
/// - Goodbye announcement via shutdown_callback (if provided)
pub async fn run(
    listener: TcpListener,
    app: Router,
    api_endpoint: &str,
    console: Arc<ConsolePrinter>,
    shutdown_notify: Arc<tokio::sync::Notify>,
    config: ServerConfig,
    shutdown_callback: Option<ShutdownCallback>,
) -> anyhow::Result<()> {
    let addr = listener.local_addr()?;

    tracing::info!(
        ?addr,
        api_endpoint = %api_endpoint,
        body_limit_mb = 200,
        "Moss HTTP server ready with 200 MB body limit configured"
    );

    // Emit HTTP server ready event
    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Ready,
        format!("HTTP server → {}", api_endpoint)
    ));

    // Create server with graceful shutdown
    let server = axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            shutdown_signal().await;
            tracing::info!("Shutdown signal received, initiating graceful shutdown");

            // Send goodbye announcement if callback provided
            if let Some(callback) = shutdown_callback {
                tracing::info!("Sending goodbye announcement before shutdown");
                callback().await;
            }
        });

    // Clone console for shutdown events
    let shutdown_console = console.clone();

    // Run server with shutdown coordination
    tokio::select! {
        result = server => {
            if let Err(e) = result {
                tracing::error!(error = ?e, "Server error");
                return Err(e.into());
            }
        }
        _ = shutdown_notify.notified() => {
            tracing::info!("Admin shutdown requested");

            shutdown_console.emit(ConsoleEvent::new(
                EventCategory::System,
                EventStatus::Shutting,
                "Admin requested".to_string()
            ));
        }
    }

    // Allow in-flight requests to complete
    tracing::info!("Waiting up to {}s for in-flight requests to complete", config.graceful_shutdown_timeout_secs);

    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Draining,
        "In-flight requests".to_string()
    ));
    tokio::time::sleep(tokio::time::Duration::from_secs(config.graceful_shutdown_timeout_secs)).await;

    tracing::info!("Moss daemon shutdown complete");

    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Stopped,
        "Shutdown complete".to_string()
    ));

    Ok(())
}
