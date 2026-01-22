//! Health monitoring business logic
//!
//! Pure domain logic for:
//! - Component health evaluation (docker, disk, memory, initialization)
//! - Overall system status determination
//! - Health check thresholds and rules
//!
//! No I/O here - delegates to metrics and docker modules.

use std::collections::HashMap;
use garden_common::{HealthCheck, ComponentHealth};

/// Check disk health based on available space
///
/// Thresholds:
/// - < 10% available: WARN
/// - >= 10% available: PASS
pub fn check_disk_health() -> HealthCheck {
    match crate::metrics::collect_stone_resources() {
        Ok(resources) => {
            let available_percent = (resources.disk.available_bytes as f32 / resources.disk.total_bytes as f32) * 100.0;
            if available_percent < 10.0 {
                HealthCheck {
                    status: garden_common::CHECK_WARN.to_string(),
                    message: Some(format!(
                        "Low disk space: {:.1}% free ({} available)",
                        available_percent,
                        resources.disk.available_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: garden_common::CHECK_PASS.to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
            message: Some(format!("Failed to check disk: {}", e)),
        },
    }
}

/// Check memory health based on usage percentage
///
/// Thresholds:
/// - > 90% used: WARN
/// - <= 90% used: PASS
pub fn check_memory_health() -> HealthCheck {
    match crate::metrics::collect_stone_resources() {
        Ok(resources) => {
            if resources.memory.used_percent > 90.0 {
                HealthCheck {
                    status: garden_common::CHECK_WARN.to_string(),
                    message: Some(format!(
                        "High memory usage: {:.1}% ({} used of {})",
                        resources.memory.used_percent,
                        resources.memory.used_friendly,
                        resources.memory.total_friendly
                    )),
                }
            } else {
                HealthCheck {
                    status: garden_common::CHECK_PASS.to_string(),
                    message: None,
                }
            }
        }
        Err(e) => HealthCheck {
            status: garden_common::CHECK_FAIL.to_string(),
            message: Some(format!("Failed to check memory: {}", e)),
        },
    }
}

/// Build disk component health with detailed metrics
///
/// Thresholds:
/// - > 95% used: unhealthy
/// - > 90% used: degraded
/// - <= 90% used: healthy
pub fn build_disk_component() -> ComponentHealth {
    let mut details = HashMap::new();

    match crate::metrics::collect_stone_resources() {
        Ok(resources) => {
            let total_gb = resources.disk.total_bytes as f64 / 1_073_741_824.0;
            let free_gb = resources.disk.available_bytes as f64 / 1_073_741_824.0;
            let usage_percent = resources.disk.used_percent;

            details.insert("free_gb".to_string(), serde_json::json!(format!("{:.1}", free_gb)));
            details.insert("total_gb".to_string(), serde_json::json!(format!("{:.1}", total_gb)));
            details.insert("usage_percent".to_string(), serde_json::json!(format!("{:.2}", usage_percent)));

            // Thresholds: >95% unhealthy, >90% degraded, else healthy
            if usage_percent > 95.0 {
                ComponentHealth::unhealthy(details)
            } else if usage_percent > 90.0 {
                ComponentHealth::degraded(details)
            } else {
                ComponentHealth::healthy(details)
            }
        }
        Err(_) => {
            details.insert("error".to_string(), serde_json::json!("Unable to collect disk metrics"));
            ComponentHealth::unhealthy(details)
        }
    }
}

/// Build memory component health with detailed metrics
///
/// Thresholds:
/// - > 95% used: unhealthy
/// - > 85% used: degraded
/// - <= 85% used: healthy
pub fn build_memory_component() -> ComponentHealth {
    let mut details = HashMap::new();

    match crate::metrics::collect_stone_resources() {
        Ok(resources) => {
            let total_gb = resources.memory.total_bytes as f64 / 1_073_741_824.0;
            let available_gb = resources.memory.available_bytes as f64 / 1_073_741_824.0;
            let usage_percent = resources.memory.used_percent;

            details.insert("available_gb".to_string(), serde_json::json!(format!("{:.1}", available_gb)));
            details.insert("total_gb".to_string(), serde_json::json!(format!("{:.1}", total_gb)));
            details.insert("usage_percent".to_string(), serde_json::json!(format!("{:.2}", usage_percent)));

            // Thresholds: >95% unhealthy, >85% degraded, else healthy
            if usage_percent > 95.0 {
                ComponentHealth::unhealthy(details)
            } else if usage_percent > 85.0 {
                ComponentHealth::degraded(details)
            } else {
                ComponentHealth::healthy(details)
            }
        }
        Err(_) => {
            details.insert("error".to_string(), serde_json::json!("Unable to collect memory metrics"));
            ComponentHealth::unhealthy(details)
        }
    }
}

/// Determine overall system health status from component statuses
///
/// Logic: worst component wins
/// - Any unhealthy → unhealthy
/// - Any degraded → degraded
/// - All healthy → healthy
pub fn determine_overall_status(components: &HashMap<String, ComponentHealth>) -> String {
    // Overall status is worst component status: unhealthy > degraded > healthy
    let mut has_unhealthy = false;
    let mut has_degraded = false;

    for component in components.values() {
        match component.status.as_str() {
            garden_common::HEALTH_UNHEALTHY => has_unhealthy = true,
            garden_common::HEALTH_DEGRADED => has_degraded = true,
            _ => {}
        }
    }

    if has_unhealthy {
        garden_common::HEALTH_UNHEALTHY.to_string()
    } else if has_degraded {
        garden_common::HEALTH_DEGRADED.to_string()
    } else {
        garden_common::HEALTH_HEALTHY.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_determine_overall_status_all_healthy() {
        let mut components = HashMap::new();
        components.insert(
            "disk".to_string(),
            ComponentHealth::healthy(HashMap::new()),
        );
        components.insert(
            "memory".to_string(),
            ComponentHealth::healthy(HashMap::new()),
        );

        let status = determine_overall_status(&components);
        assert_eq!(status, garden_common::HEALTH_HEALTHY);
    }

    #[test]
    fn test_determine_overall_status_one_degraded() {
        let mut components = HashMap::new();
        components.insert(
            "disk".to_string(),
            ComponentHealth::healthy(HashMap::new()),
        );
        components.insert(
            "memory".to_string(),
            ComponentHealth::degraded(HashMap::new()),
        );

        let status = determine_overall_status(&components);
        assert_eq!(status, garden_common::HEALTH_DEGRADED);
    }

    #[test]
    fn test_determine_overall_status_one_unhealthy() {
        let mut components = HashMap::new();
        components.insert(
            "disk".to_string(),
            ComponentHealth::degraded(HashMap::new()),
        );
        components.insert(
            "memory".to_string(),
            ComponentHealth::unhealthy(HashMap::new()),
        );

        let status = determine_overall_status(&components);
        assert_eq!(status, garden_common::HEALTH_UNHEALTHY);
    }
}
