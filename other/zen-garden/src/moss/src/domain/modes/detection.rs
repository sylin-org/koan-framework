//! Detection orchestration with caching and stability tracking
//!
//! Coordinates multiple detection methods for adopted offerings:
//! - Parallel execution with concurrency limits
//! - Detection result caching with TTL
//! - Stability tracking (consecutive successes required before adoption)
//! - Proactive cache refresh

use anyhow::{Context, Result};
use dashmap::DashMap;
use std::sync::Arc;
use std::time::{Duration, Instant};
use garden_common::manifests::{DetectionMethod, DetectionRule, OfferingManifest};
use crate::docker::DockerManager;
use crate::infra::detection::{
    detect_by_command, detect_by_container_inspect, detect_by_http_probe, DetectionResult,
};

/// Detection orchestrator with caching and stability tracking
pub struct DetectionOrchestrator {
    /// Docker manager for container detection
    docker: Arc<DockerManager>,

    /// Detection result cache
    cache: Arc<DashMap<String, CachedDetection>>,

    /// Stability tracker (offering -> consecutive success/failure count)
    stability: Arc<DashMap<String, StabilityState>>,

    /// Maximum concurrent detections (default: 10)
    /// Reserved for future parallel detection implementation
    #[allow(dead_code)]
    _max_concurrent: usize,
}

/// Cached detection result
#[derive(Debug, Clone)]
struct CachedDetection {
    result: DetectionResult,
    cached_at: Instant,
    ttl: Duration,
}

/// Stability tracking state
#[derive(Debug, Clone)]
struct StabilityState {
    consecutive_successes: u8,
    consecutive_failures: u8,
    last_state: bool, // true = detected, false = not detected
}

impl DetectionOrchestrator {
    /// Create new detection orchestrator
    pub fn new(docker: Arc<DockerManager>) -> Self {
        Self {
            docker,
            cache: Arc::new(DashMap::new()),
            stability: Arc::new(DashMap::new()),
            _max_concurrent: 10,
        }
    }

    /// Detect service using manifest rules
    ///
    /// Returns detection result with stability tracking.
    /// Result is cached according to rule TTL.
    pub async fn detect(
        &self,
        manifest: &OfferingManifest,
    ) -> Result<AggregatedDetectionResult> {
        if manifest.detection.is_empty() {
            return Ok(AggregatedDetectionResult {
                detected: false,
                stable: false,
                version: None,
                methods_tried: 0,
                details: "No detection rules configured".into(),
            });
        }

        // Try detection rules in order (first match wins)
        let mut methods_tried = 0;

        for rule in &manifest.detection {
            methods_tried += 1;

            let cache_key = format!("{}:{:?}", manifest.name, rule.method);

            // Check cache first
            if let Some(cached) = self.cache.get(&cache_key) {
                if cached.cached_at.elapsed() < cached.ttl {
                    tracing::debug!(
                        offering = %manifest.name,
                        method = ?rule.method,
                        "Using cached detection result"
                    );

                    let stable = self.check_stability(&manifest.name, cached.result.detected, rule);
                    return Ok(AggregatedDetectionResult {
                        detected: cached.result.detected,
                        stable,
                        version: cached.result.version.clone(),
                        methods_tried,
                        details: cached.result.details.clone(),
                    });
                }
            }

            // Execute detection
            let result = self.execute_detection(&manifest.name, rule).await?;

            // Update cache
            let ttl = Duration::from_secs(rule.cache_ttl_secs.unwrap_or(300));
            self.cache.insert(
                cache_key.clone(),
                CachedDetection {
                    result: result.clone(),
                    cached_at: Instant::now(),
                    ttl,
                },
            );

            // Update stability tracking
            let stable = self.check_stability(&manifest.name, result.detected, rule);

            if result.detected {
                return Ok(AggregatedDetectionResult {
                    detected: true,
                    stable,
                    version: result.version,
                    methods_tried,
                    details: result.details,
                });
            }
        }

        // No rules matched
        Ok(AggregatedDetectionResult {
            detected: false,
            stable: false,
            version: None,
            methods_tried,
            details: "No detection rules matched".into(),
        })
    }

    /// Execute a single detection rule
    async fn execute_detection(
        &self,
        _offering: &str,
        rule: &DetectionRule,
    ) -> Result<DetectionResult> {
        let timeout = Duration::from_secs(5); // Default timeout

        match rule.method {
            DetectionMethod::Command => {
                if let garden_common::manifests::DetectionConfig::Command(ref config) = rule.config {
                    detect_by_command(config, timeout)
                        .await
                        .context("Command detection failed")
                } else {
                    anyhow::bail!("Invalid detection config for command method")
                }
            }
            DetectionMethod::ContainerInspect => {
                if let garden_common::manifests::DetectionConfig::ContainerInspect(ref config) =
                    rule.config
                {
                    detect_by_container_inspect(&self.docker, config)
                        .await
                        .context("Container inspection failed")
                } else {
                    anyhow::bail!("Invalid detection config for container_inspect method")
                }
            }
            DetectionMethod::HttpProbe => {
                if let garden_common::manifests::DetectionConfig::HttpProbe(ref config) =
                    rule.config
                {
                    detect_by_http_probe(config)
                        .await
                        .context("HTTP probe failed")
                } else {
                    anyhow::bail!("Invalid detection config for http_probe method")
                }
            }
        }
    }

    /// Check and update stability tracking
    ///
    /// Returns true if the offering is stable (enough consecutive detections).
    fn check_stability(&self, offering: &str, detected: bool, rule: &DetectionRule) -> bool {
        let threshold = rule.stability_threshold.unwrap_or(2);

        let mut entry = self.stability.entry(offering.to_string()).or_insert(StabilityState {
            consecutive_successes: 0,
            consecutive_failures: 0,
            last_state: false,
        });

        if detected {
            if entry.last_state {
                // Continue success streak
                entry.consecutive_successes += 1;
                entry.consecutive_failures = 0;
            } else {
                // State changed to detected
                entry.consecutive_successes = 1;
                entry.consecutive_failures = 0;
                entry.last_state = true;
            }

            entry.consecutive_successes >= threshold
        } else {
            if !entry.last_state {
                // Continue failure streak
                entry.consecutive_failures += 1;
                entry.consecutive_successes = 0;
            } else {
                // State changed to not detected
                entry.consecutive_failures = 1;
                entry.consecutive_successes = 0;
                entry.last_state = false;
            }

            false
        }
    }

    /// Clear cache for specific offering
    pub fn invalidate_cache(&self, offering: &str) {
        self.cache.retain(|k, _| !k.starts_with(&format!("{}:", offering)));
    }

    /// Clear all cached detections
    pub fn clear_cache(&self) {
        self.cache.clear();
    }

    /// Reset stability tracking for offering
    pub fn reset_stability(&self, offering: &str) {
        self.stability.remove(offering);
    }
}

/// Aggregated detection result with stability tracking
#[derive(Debug, Clone)]
pub struct AggregatedDetectionResult {
    /// Whether service was detected
    pub detected: bool,
    /// Whether detection is stable (passed stability threshold)
    pub stable: bool,
    /// Detected version (if available)
    pub version: Option<String>,
    /// Number of detection methods tried
    pub methods_tried: usize,
    /// Human-readable details
    pub details: String,
}

#[cfg(test)]
mod tests {
    use super::*;
    use garden_common::manifests::{CommandDetection, DetectionConfig};

    #[test]
    fn test_stability_tracking() {
        let docker = Arc::new(DockerManager::new().unwrap());
        let orchestrator = DetectionOrchestrator::new(docker);

        let rule = DetectionRule {
            method: DetectionMethod::Command,
            config: DetectionConfig::Command(CommandDetection {
                command: "test".into(),
                expected_pattern: None,
                expected_exit_code: None,
            }),
            stability_threshold: Some(2),
            cache_ttl_secs: None,
        };

        // First detection - not stable yet
        assert!(!orchestrator.check_stability("test", true, &rule));

        // Second detection - now stable
        assert!(orchestrator.check_stability("test", true, &rule));

        // Third detection - remains stable
        assert!(orchestrator.check_stability("test", true, &rule));

        // Detection fails - not stable anymore
        assert!(!orchestrator.check_stability("test", false, &rule));
    }

    #[test]
    fn test_cache_invalidation() {
        let docker = Arc::new(DockerManager::new().unwrap());
        let orchestrator = DetectionOrchestrator::new(docker);

        orchestrator.cache.insert(
            "test:command".into(),
            CachedDetection {
                result: DetectionResult {
                    detected: true,
                    version: None,
                    details: "test".into(),
                },
                cached_at: Instant::now(),
                ttl: Duration::from_secs(300),
            },
        );

        assert_eq!(orchestrator.cache.len(), 1);

        orchestrator.invalidate_cache("test");

        assert_eq!(orchestrator.cache.len(), 0);
    }
}
