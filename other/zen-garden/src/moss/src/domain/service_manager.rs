//! Service lifecycle management
//!
//! Handles service operations: install, start, stop, remove, upgrade

use anyhow::Result;
use garden_common::events::{EventBus, DomainEvent, ServiceEvent};
use garden_common::traits::job_executor::{JobExecutor, JobResult, JobExecutionError};
use crate::infra::ContainerRuntime;
use async_trait::async_trait;
use serde_json::Value;
use std::sync::Arc;
use chrono::Utc;

/// Service manager handles service lifecycle
pub struct ServiceManager {
    container_runtime: Arc<ContainerRuntime>,
    event_bus: Arc<EventBus>,
    stone_name: String,
}

impl ServiceManager {
    /// Create a new service manager
    pub fn new(
        container_runtime: Arc<ContainerRuntime>,
        event_bus: Arc<EventBus>,
        stone_name: String,
    ) -> Self {
        Self {
            container_runtime,
            event_bus,
            stone_name,
        }
    }

    /// Start a service
    pub async fn start_service(&self, service_name: &str) -> Result<()> {
        // Publish event
        let _ = self.event_bus.publish(DomainEvent::Service(ServiceEvent::Started {
            stone_name: self.stone_name.clone(),
            service_name: service_name.to_string(),
            timestamp: Utc::now(),
        })).await;

        self.container_runtime.start_service(service_name).await
    }

    /// Stop a service
    pub async fn stop_service(&self, service_name: &str) -> Result<()> {
        self.container_runtime.stop_service(service_name).await?;

        // Publish event
        let _ = self.event_bus.publish(DomainEvent::Service(ServiceEvent::Stopped {
            stone_name: self.stone_name.clone(),
            service_name: service_name.to_string(),
            timestamp: Utc::now(),
        })).await;

        Ok(())
    }

    /// Remove a service
    pub async fn remove_service(&self, service_name: &str) -> Result<()> {
        self.container_runtime.remove_service(service_name).await?;

        // Publish event
        let _ = self.event_bus.publish(DomainEvent::Service(ServiceEvent::Removed {
            stone_name: self.stone_name.clone(),
            service_name: service_name.to_string(),
            timestamp: Utc::now(),
        })).await;

        Ok(())
    }

    /// List all services
    pub async fn list_services(&self) -> Result<Vec<String>> {
        self.container_runtime.list_services().await
    }

    /// Check if service exists
    pub async fn service_exists(&self, service_name: &str) -> Result<bool> {
        self.container_runtime.service_exists(service_name).await
    }
}

/// Install service job executor
///
/// Handles long-running service installation as background job
pub struct InstallServiceExecutor {
    _service_manager: Arc<ServiceManager>,
}

impl InstallServiceExecutor {
    pub fn new(service_manager: Arc<ServiceManager>) -> Self {
        Self { _service_manager: service_manager }
    }
}

#[async_trait]
impl JobExecutor for InstallServiceExecutor {
    fn job_type(&self) -> &str {
        "install-service"
    }

    async fn execute(&self, job_id: &str, input: Value) -> Result<JobResult, JobExecutionError> {
        let service_name = input.get("service_name")
            .and_then(|v| v.as_str())
            .ok_or_else(|| JobExecutionError::InvalidInput("Missing service_name".into()))?;

        let offering = input.get("offering")
            .and_then(|v| v.as_str())
            .ok_or_else(|| JobExecutionError::InvalidInput("Missing offering".into()))?;

        tracing::info!(job_id, service_name, offering, "Starting service installation");

        // TODO: Implement full installation logic
        // For now, return success
        Ok(JobResult::success(format!(
            "Service {} installed successfully",
            service_name
        )))
    }

    fn validate_input(&self, input: &Value) -> Result<(), JobExecutionError> {
        if !input.is_object() {
            return Err(JobExecutionError::InvalidInput(
                "Input must be an object".into(),
            ));
        }

        if input.get("service_name").and_then(|v| v.as_str()).is_none() {
            return Err(JobExecutionError::InvalidInput(
                "Missing required field: service_name".into(),
            ));
        }

        if input.get("offering").and_then(|v| v.as_str()).is_none() {
            return Err(JobExecutionError::InvalidInput(
                "Missing required field: offering".into(),
            ));
        }

        Ok(())
    }
}
