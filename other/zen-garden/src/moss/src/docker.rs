use anyhow::{Context, Result};
use bollard::container::{
    Config, CreateContainerOptions, InspectContainerOptions, ListContainersOptions,
    LogsOptions, RemoveContainerOptions, StartContainerOptions, StatsOptions, StopContainerOptions,
};
use bollard::image::CreateImageOptions;
use bollard::models::{ContainerCreateResponse, HealthStatusEnum, HostConfig, PortBinding};
use bollard::Docker;
use futures_util::stream::{Stream, StreamExt, TryStreamExt};
use std::collections::HashMap;
use std::pin::Pin;
use garden_common::{ServiceHealthStatus, ServiceStatus};

pub struct DockerManager {
    docker: Docker,
}

impl DockerManager {
    pub fn new() -> Result<Self> {
        #[cfg(target_os = "windows")]
        let docker = {
            tracing::debug!("Connecting to Docker via Windows named pipe");
            Docker::connect_with_named_pipe_defaults()
                .context("Failed to connect to Docker daemon via named pipe (is Docker Desktop running?)")?
        };
        
        #[cfg(not(target_os = "windows"))]
        let docker = {
            tracing::debug!("Connecting to Docker via Unix socket");
            Docker::connect_with_socket_defaults()
                .context("Failed to connect to Docker daemon via Unix socket")?
        };
        
        Ok(Self { docker })
    }

    /// Check if Docker daemon is available and responsive
    pub async fn is_healthy(&self) -> bool {
        self.docker.ping().await.is_ok()
    }

    /// Stop a service container
    pub async fn stop_service(&self, name: &str) -> Result<()> {
        let container_name = format!("zen-offering-{}", name);
        tracing::info!(service = %name, "Stopping service via Docker API");

        self.docker
            .stop_container(&container_name, None::<StopContainerOptions>)
            .await
            .context("Failed to stop container")?;

        tracing::info!(service = %name, "Service stopped successfully");
        Ok(())
    }

    /// Start a service container
    pub async fn start_service(&self, name: &str) -> Result<()> {
        let container_name = format!("zen-offering-{}", name);
        tracing::info!(service = %name, "Starting service via Docker API");

        self.docker
            .start_container(&container_name, None::<StartContainerOptions<String>>)
            .await
            .context("Failed to start container")?;

        tracing::info!(service = %name, "Service started successfully");
        Ok(())
    }

    pub async fn install_service(
        &self,
        name: &str,
        image: &str,
        ports: Vec<(u16, u16)>,
        env: Vec<String>,
        volumes: Vec<(String, String)>,
    ) -> Result<()> {
        tracing::info!(service = %name, image = %image, "Installing service via Docker API");

        // Prefix container name with "zen-offering-" to identify as Zen Garden offering
        // Note: zen-companion-* prefix is reserved for sidecars/companion containers
        let container_name = format!("zen-offering-{}", name);

        // Check if container already exists
        if self.container_exists(&container_name).await? {
            anyhow::bail!("Container '{}' already exists", container_name);
        }

        // Pull image if not present
        self.pull_image(image).await?;

        // Configure port bindings
        let mut port_bindings = HashMap::new();
        for (host_port, container_port) in &ports {
            port_bindings.insert(
                format!("{}/tcp", container_port),
                Some(vec![PortBinding {
                    host_ip: Some("0.0.0.0".to_string()),
                    host_port: Some(host_port.to_string()),
                }]),
            );
        }

        // Configure volumes
        let mut binds = Vec::new();
        for (host_path, container_path) in &volumes {
            binds.push(format!("{}:{}", host_path, container_path));
        }

        let host_config = HostConfig {
            port_bindings: Some(port_bindings),
            binds: Some(binds),
            restart_policy: Some(bollard::models::RestartPolicy {
                name: Some(bollard::models::RestartPolicyNameEnum::UNLESS_STOPPED),
                maximum_retry_count: None,
            }),
            ..Default::default()
        };

        let config = Config {
            image: Some(image),
            env: Some(env.iter().map(|s| s.as_str()).collect()),
            host_config: Some(host_config),
            ..Default::default()
        };

        // Create container
        let response: ContainerCreateResponse = self
            .docker
            .create_container(
                Some(CreateContainerOptions {
                    name: &container_name,
                    platform: None,
                }),
                config,
            )
            .await
            .context("Failed to create container")?;

        tracing::info!(container_id = %response.id, container_name = %container_name, "Container created");

        // Start container
        self.docker
            .start_container(&container_name, None::<StartContainerOptions<String>>)
            .await
            .context("Failed to start container")?;

        tracing::info!(service = %name, container_name = %container_name, "Service started successfully");
        Ok(())
    }

    pub async fn remove_service(&self, name: &str) -> Result<()> {
        tracing::info!(service = %name, "Removing service via Docker API");

        let container_name = format!("zen-offering-{}", name);

        if !self.container_exists(&container_name).await? {
            anyhow::bail!("Container '{}' does not exist", container_name);
        }

        // Stop container
        self.docker
            .stop_container(&container_name, None::<StopContainerOptions>)
            .await
            .context("Failed to stop container")?;

        // Remove container
        self.docker
            .remove_container(
                &container_name,
                Some(RemoveContainerOptions {
                    v: true, // Remove associated volumes
                    force: true,
                    link: false,
                }),
            )
            .await
            .context("Failed to remove container")?;

        tracing::info!(service = %name, container_name = %container_name, "Service removed successfully");
        Ok(())
    }

    #[allow(dead_code)]
    pub async fn upgrade_service(
        &self,
        name: &str,
        new_image: &str,
        ports: Vec<(u16, u16)>,
        env: Vec<String>,
        volumes: Vec<(String, String)>,
    ) -> Result<()> {
        let container_name = format!("zen-offering-{}", name);
        tracing::info!(service = %name, new_image = %new_image, "Upgrading service");

        // Pull new image
        self.pull_image(new_image).await?;

        // Stop and remove old container
        self.remove_service(name).await?;

        // Create and start new container
        self.install_service(name, new_image, ports, env, volumes)
            .await?;

        tracing::info!(service = %name, container_name = %container_name, "Service upgraded successfully");
        Ok(())
    }

    async fn container_exists(&self, name: &str) -> Result<bool> {
        let filters = HashMap::from([("name".to_string(), vec![name.to_string()])]);
        let options = ListContainersOptions {
            all: true,
            filters,
            ..Default::default()
        };

        let containers = self
            .docker
            .list_containers(Some(options))
            .await
            .context("Failed to list containers")?;

        Ok(containers.iter().any(|c| {
            c.names
                .as_ref()
                .map(|names| names.iter().any(|n| n.trim_start_matches('/') == name))
                .unwrap_or(false)
        }))
    }

    /// Check if a zen-offering container exists for the given offering name
    pub async fn zen_container_exists(&self, offering: &str) -> Result<bool> {
        let container_name = format!("zen-offering-{}", offering);
        self.container_exists(&container_name).await
    }

    /// Get the Docker image string for a zen-offering container (e.g., "mongo:7")
    pub async fn get_service_image(&self, name: &str) -> Result<String> {
        let container_name = format!("zen-offering-{}", name);
        let inspect = self
            .docker
            .inspect_container(&container_name, None::<InspectContainerOptions>)
            .await
            .context(format!("Failed to inspect container '{}'", container_name))?;

        let config = inspect.config.context("Container has no config")?;
        let image = config.image.unwrap_or_else(|| "<unknown>".to_string());
        Ok(image)
    }

    async fn pull_image(&self, image: &str) -> Result<()> {
        tracing::info!(image = %image, "Pulling Docker image");

        let options = CreateImageOptions {
            from_image: image,
            ..Default::default()
        };

        let mut stream = self.docker.create_image(Some(options), None, None);

        while let Some(result) = stream.next().await {
            match result {
                Ok(info) => {
                    if let Some(status) = info.status {
                        tracing::debug!(image = %image, status = %status, "Pull progress");
                    }
                }
                Err(e) => {
                    anyhow::bail!("Failed to pull image '{}': {}", image, e);
                }
            }
        }

        tracing::info!(image = %image, "Image pulled successfully");
        Ok(())
    }

    /// Get the status of a service by checking its Docker container
    pub async fn get_service_status(&self, name: &str) -> Result<ServiceStatus> {
        let container_name = format!("zen-offering-{}", name);
        
        let inspect = self
            .docker
            .inspect_container(&container_name, None::<InspectContainerOptions>)
            .await
            .context(format!("Failed to inspect container '{}'", container_name))?;

        let state = inspect.state.context("Container has no state")?;
        
        let status = if state.running.unwrap_or(false) {
            ServiceStatus::Running
        } else if state.paused.unwrap_or(false) {
            ServiceStatus::Stopped
        } else if state.restarting.unwrap_or(false) {
            ServiceStatus::Degraded
        } else {
            ServiceStatus::Stopped
        };

        Ok(status)
    }

    /// Get the health status of a service by checking its Docker container health
    pub async fn get_service_health(&self, name: &str) -> Result<ServiceHealthStatus> {
        let container_name = format!("zen-offering-{}", name);
        
        let inspect = self
            .docker
            .inspect_container(&container_name, None::<InspectContainerOptions>)
            .await
            .context(format!("Failed to inspect container '{}'", container_name))?;

        let state = inspect.state.context("Container has no state")?;
        
        // Check if container is running first
        if !state.running.unwrap_or(false) {
            return Ok(ServiceHealthStatus::Offline);
        }

        // Check Docker health check status if available
        if let Some(health) = state.health {
            if let Some(status) = health.status {
                return Ok(match status {
                    HealthStatusEnum::HEALTHY => ServiceHealthStatus::Healthy,
                    HealthStatusEnum::UNHEALTHY => ServiceHealthStatus::Degraded,
                    HealthStatusEnum::STARTING => ServiceHealthStatus::Degraded,
                    HealthStatusEnum::NONE | HealthStatusEnum::EMPTY => ServiceHealthStatus::Healthy,
                });
            }
        }

        // If no health check configured, assume healthy if running
        Ok(ServiceHealthStatus::Healthy)
    }

    /// List all zen-offering-prefixed containers
    /// Note: Does not include zen-companion-* sidecars
    pub async fn list_zen_containers(&self) -> Result<Vec<String>> {
        let filters = HashMap::from([("name".to_string(), vec!["zen-offering-".to_string()])]);
        let options = ListContainersOptions {
            all: true,
            filters,
            ..Default::default()
        };

        let containers = self
            .docker
            .list_containers(Some(options))
            .await
            .context("Failed to list containers")?;

        let names = containers
            .into_iter()
            .filter_map(|c| {
                c.names.and_then(|names| {
                    names.into_iter().find_map(|n| {
                        let trimmed = n.trim_start_matches('/');
                        if trimmed.starts_with("zen-offering-") {
                            Some(trimmed.strip_prefix("zen-offering-").unwrap_or(trimmed).to_string())
                        } else {
                            None
                        }
                    })
                })
            })
            .collect();

        Ok(names)
    }

    /// Get resource metrics for a specific container
    pub async fn get_container_stats(&self, name: &str) -> Result<garden_common::ContainerResources> {
        let container_name = format!("zen-offering-{}", name);
        
        let stats = self
            .docker
            .stats(&container_name, Some(StatsOptions {
                stream: false,
                one_shot: true,
            }))
            .try_next()
            .await
            .context("Failed to get container stats")?
            .ok_or_else(|| anyhow::anyhow!("No stats available for container"))?;

        // Calculate CPU percentage
        let cpu_delta = stats.cpu_stats.cpu_usage.total_usage as f64
            - stats.precpu_stats.cpu_usage.total_usage as f64;
        let system_delta = stats.cpu_stats.system_cpu_usage.unwrap_or(0) as f64
            - stats.precpu_stats.system_cpu_usage.unwrap_or(0) as f64;
        let cpu_percent = if system_delta > 0.0 && cpu_delta > 0.0 {
            let num_cpus = stats.cpu_stats.online_cpus.unwrap_or(1) as f64;
            (cpu_delta / system_delta) * num_cpus * 100.0
        } else {
            0.0
        };

        // Memory metrics
        let memory_bytes = stats.memory_stats.usage.unwrap_or(0);
        let memory_limit = stats.memory_stats.limit.unwrap_or(0);
        let memory_percent = if memory_limit > 0 {
            (memory_bytes as f64 / memory_limit as f64 * 100.0) as f32
        } else {
            0.0
        };

        // Network I/O
        let (network_rx_bytes, network_tx_bytes) = if let Some(networks) = stats.networks {
            networks.values().fold((0u64, 0u64), |(rx, tx), net| {
                (rx + net.rx_bytes, tx + net.tx_bytes)
            })
        } else {
            (0, 0)
        };

        // Block I/O
        let (block_read_bytes, block_write_bytes) = if let Some(io_stats) = stats.blkio_stats.io_service_bytes_recursive {
            io_stats.iter().fold((0u64, 0u64), |(read, write), entry| {
                match entry.op.as_str() {
                    "read" | "Read" => (read + entry.value, write),
                    "write" | "Write" => (read, write + entry.value),
                    _ => (read, write),
                }
            })
        } else {
            (0, 0)
        };

        // Container uptime (calculate from started_at timestamp)
        let uptime_seconds = self.get_container_uptime(&container_name).await.unwrap_or(0);

        Ok(garden_common::ContainerResources {
            cpu_percent: cpu_percent as f32,
            cpu_friendly: format!("{:.2}%", cpu_percent),
            memory_bytes,
            memory_limit_bytes: memory_limit,
            memory_percent,
            memory_friendly: garden_common::format_bytes(memory_bytes),
            memory_limit_friendly: garden_common::format_bytes(memory_limit),
            network_rx_bytes,
            network_tx_bytes,
            network_rx_friendly: garden_common::format_bytes(network_rx_bytes),
            network_tx_friendly: garden_common::format_bytes(network_tx_bytes),
            block_read_bytes,
            block_write_bytes,
            block_read_friendly: garden_common::format_bytes(block_read_bytes),
            block_write_friendly: garden_common::format_bytes(block_write_bytes),
            uptime_seconds,
            uptime_friendly: garden_common::format_uptime(uptime_seconds),
        })
    }

    /// Get container uptime in seconds
    async fn get_container_uptime(&self, container_name: &str) -> Result<u64> {
        let inspect = self
            .docker
            .inspect_container(container_name, None::<InspectContainerOptions>)
            .await
            .context("Failed to inspect container")?;

        if let Some(state) = inspect.state {
            if let Some(started_at) = state.started_at {
                // Parse ISO 8601 timestamp
                if let Ok(started) = chrono::DateTime::parse_from_rfc3339(&started_at) {
                    let now = chrono::Utc::now();
                    let duration = now.signed_duration_since(started);
                    return Ok(duration.num_seconds().max(0) as u64);
                }
            }
        }

        Ok(0)
    }

    /// Stream logs from a container in real-time (follow mode)
    pub fn get_logs_stream(
        &self,
        name: &str,
        timestamps: bool,
    ) -> Pin<Box<dyn Stream<Item = Result<LogLine>> + Send + 'static>> {
        let container_name = format!("zen-offering-{}", name);
        let docker = self.docker.clone();
        
        Box::pin(async_stream::stream! {
            let options = LogsOptions::<String> {
                follow: true,
                stdout: true,
                stderr: true,
                timestamps,
                ..Default::default()
            };

            let mut stream = docker.logs(&container_name, Some(options));
            
            while let Some(result) = stream.next().await {
                match result {
                    Ok(output) => {
                        let log_line = LogLine {
                            timestamp: if timestamps {
                                Some(chrono::Utc::now().to_rfc3339())
                            } else {
                                None
                            },
                            stream: match output {
                                bollard::container::LogOutput::StdOut { .. } => "stdout".to_string(),
                                bollard::container::LogOutput::StdErr { .. } => "stderr".to_string(),
                                _ => "console".to_string(),
                            },
                            log: output.to_string(),
                        };
                        yield Ok(log_line);
                    }
                    Err(e) => {
                        yield Err(anyhow::anyhow!("Docker logs error: {}", e));
                        break;
                    }
                }
            }
        })
    }
}

#[derive(Debug, Clone, serde::Serialize)]
pub struct LogLine {
    pub timestamp: Option<String>,
    pub stream: String,
    pub log: String,
}
