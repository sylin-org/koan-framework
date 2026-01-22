//! Container runtime abstraction
//!
//! Wraps Podman/Docker operations for service management.
//! Currently uses bollard (Docker API) - could support Podman in future.

use anyhow::{Context, Result};
use garden_common::{ServiceStatus, ServiceHealthStatus, ContainerResources};
use crate::docker::DockerManager;

/// Container runtime for service management
///
/// Abstracts away Docker/Podman specifics from domain layer.
/// For v0.1.0, delegates to existing DockerManager.
pub struct ContainerRuntime {
    docker: DockerManager,
}

impl ContainerRuntime {
    /// Create a new container runtime
    pub fn new() -> Result<Self> {
        let docker = DockerManager::new()
            .context("Failed to initialize container runtime")?;

        Ok(Self { docker })
    }

    /// Check if container runtime is healthy
    pub async fn is_healthy(&self) -> bool {
        self.docker.is_healthy().await
    }

    /// Check if a service container exists
    pub async fn service_exists(&self, service_name: &str) -> Result<bool> {
        self.docker.zen_container_exists(service_name).await
    }

    /// Get service status (Running, Stopped, etc.)
    pub async fn get_service_status(&self, service_name: &str) -> Result<ServiceStatus> {
        self.docker.get_service_status(service_name).await
    }

    /// Get service health status (Healthy, Degraded, Offline)
    pub async fn get_service_health(&self, service_name: &str) -> Result<ServiceHealthStatus> {
        self.docker.get_service_health(service_name).await
    }

    /// List all zen service containers
    pub async fn list_services(&self) -> Result<Vec<String>> {
        self.docker.list_zen_containers().await
    }

    /// Get container resource usage
    pub async fn get_stats(&self, service_name: &str) -> Result<ContainerResources> {
        self.docker.get_container_stats(service_name).await
    }

    /// Start a service container
    pub async fn start_service(&self, service_name: &str) -> Result<()> {
        self.docker.start_service(service_name, None).await
    }

    /// Stop a service container
    pub async fn stop_service(&self, service_name: &str) -> Result<()> {
        self.docker.stop_service(service_name, None).await
    }

    /// Remove a service container
    pub async fn remove_service(&self, service_name: &str) -> Result<()> {
        self.docker.remove_service(service_name, None).await
    }

    /// Get the image used by a service
    pub async fn get_service_image(&self, service_name: &str) -> Result<String> {
        self.docker.get_service_image(service_name).await
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    #[ignore] // Requires Docker daemon
    async fn test_container_runtime_health() {
        let runtime = ContainerRuntime::new();
        if let Ok(rt) = runtime {
            let healthy = rt.is_healthy().await;
            // Just test that we can call it
            let _ = healthy;
        }
    }
}
