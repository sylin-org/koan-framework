//! Domain events for Zen Garden system
//!
//! All events are JSON-serializable for SSE streaming and audit logs.

use serde::{Deserialize, Serialize};
use chrono::{DateTime, Utc};

/// Top-level domain event
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_category", content = "event")]
pub enum DomainEvent {
    Service(ServiceEvent),
    Registry(RegistryEvent),
    Job(JobEvent),
    Discovery(DiscoveryEvent),
}

impl DomainEvent {
    /// Get event timestamp
    pub fn timestamp(&self) -> DateTime<Utc> {
        match self {
            DomainEvent::Service(e) => e.timestamp(),
            DomainEvent::Registry(e) => e.timestamp(),
            DomainEvent::Job(e) => e.timestamp(),
            DomainEvent::Discovery(e) => e.timestamp(),
        }
    }

    /// Get event stone name (if applicable)
    pub fn stone_name(&self) -> Option<&str> {
        match self {
            DomainEvent::Service(e) => Some(e.stone_name()),
            DomainEvent::Registry(e) => Some(e.stone_name()),
            DomainEvent::Job(e) => e.stone_name(),
            DomainEvent::Discovery(e) => Some(e.stone_name()),
        }
    }

    /// Convert to JSON for SSE
    pub fn to_json(&self) -> Result<String, serde_json::Error> {
        serde_json::to_string(self)
    }
}

// ============================================================================
// Service Events
// ============================================================================

/// Service lifecycle events
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_type")]
pub enum ServiceEvent {
    /// Service installation initiated
    InstallStarted {
        stone_name: String,
        service_name: String,
        offering: String,
        timestamp: DateTime<Utc>,
    },

    /// Service installation in progress (image pull, container create)
    InstallProgress {
        stone_name: String,
        service_name: String,
        progress_message: String,
        timestamp: DateTime<Utc>,
    },

    /// Service installation completed successfully
    InstallCompleted {
        stone_name: String,
        service_name: String,
        offering: String,
        version: String,
        timestamp: DateTime<Utc>,
    },

    /// Service installation failed
    InstallFailed {
        stone_name: String,
        service_name: String,
        offering: String,
        error: String,
        timestamp: DateTime<Utc>,
    },

    /// Service started
    Started {
        stone_name: String,
        service_name: String,
        timestamp: DateTime<Utc>,
    },

    /// Service stopped
    Stopped {
        stone_name: String,
        service_name: String,
        timestamp: DateTime<Utc>,
    },

    /// Service removed
    Removed {
        stone_name: String,
        service_name: String,
        timestamp: DateTime<Utc>,
    },

    /// Service health changed
    HealthChanged {
        stone_name: String,
        service_name: String,
        old_health: String,
        new_health: String,
        timestamp: DateTime<Utc>,
    },
}

impl ServiceEvent {
    pub fn timestamp(&self) -> DateTime<Utc> {
        match self {
            ServiceEvent::InstallStarted { timestamp, .. } => *timestamp,
            ServiceEvent::InstallProgress { timestamp, .. } => *timestamp,
            ServiceEvent::InstallCompleted { timestamp, .. } => *timestamp,
            ServiceEvent::InstallFailed { timestamp, .. } => *timestamp,
            ServiceEvent::Started { timestamp, .. } => *timestamp,
            ServiceEvent::Stopped { timestamp, .. } => *timestamp,
            ServiceEvent::Removed { timestamp, .. } => *timestamp,
            ServiceEvent::HealthChanged { timestamp, .. } => *timestamp,
        }
    }

    pub fn stone_name(&self) -> &str {
        match self {
            ServiceEvent::InstallStarted { stone_name, .. } => stone_name,
            ServiceEvent::InstallProgress { stone_name, .. } => stone_name,
            ServiceEvent::InstallCompleted { stone_name, .. } => stone_name,
            ServiceEvent::InstallFailed { stone_name, .. } => stone_name,
            ServiceEvent::Started { stone_name, .. } => stone_name,
            ServiceEvent::Stopped { stone_name, .. } => stone_name,
            ServiceEvent::Removed { stone_name, .. } => stone_name,
            ServiceEvent::HealthChanged { stone_name, .. } => stone_name,
        }
    }
}

// ============================================================================
// Registry Events
// ============================================================================

/// Lantern registry events
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_type")]
pub enum RegistryEvent {
    /// Stone came online (first heartbeat)
    StoneOnline {
        stone_name: String,
        endpoint: String,
        timestamp: DateTime<Utc>,
    },

    /// Stone went offline (TTL expired)
    StoneOffline {
        stone_name: String,
        endpoint: String,
        last_seen: DateTime<Utc>,
        timestamp: DateTime<Utc>,
    },

    /// Stone heartbeat received
    Heartbeat {
        stone_name: String,
        endpoint: String,
        timestamp: DateTime<Utc>,
    },

    /// Service registered to stone
    ServiceRegistered {
        stone_name: String,
        service_name: String,
        service_type: String,
        timestamp: DateTime<Utc>,
    },

    /// Service unregistered from stone
    ServiceUnregistered {
        stone_name: String,
        service_name: String,
        service_type: String,
        timestamp: DateTime<Utc>,
    },
}

impl RegistryEvent {
    pub fn timestamp(&self) -> DateTime<Utc> {
        match self {
            RegistryEvent::StoneOnline { timestamp, .. } => *timestamp,
            RegistryEvent::StoneOffline { timestamp, .. } => *timestamp,
            RegistryEvent::Heartbeat { timestamp, .. } => *timestamp,
            RegistryEvent::ServiceRegistered { timestamp, .. } => *timestamp,
            RegistryEvent::ServiceUnregistered { timestamp, .. } => *timestamp,
        }
    }

    pub fn stone_name(&self) -> &str {
        match self {
            RegistryEvent::StoneOnline { stone_name, .. } => stone_name,
            RegistryEvent::StoneOffline { stone_name, .. } => stone_name,
            RegistryEvent::Heartbeat { stone_name, .. } => stone_name,
            RegistryEvent::ServiceRegistered { stone_name, .. } => stone_name,
            RegistryEvent::ServiceUnregistered { stone_name, .. } => stone_name,
        }
    }
}

// ============================================================================
// Job Events
// ============================================================================

/// Background job events
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_type")]
pub enum JobEvent {
    /// Job created and queued
    Created {
        job_id: String,
        job_type: String,
        stone_name: Option<String>,
        timestamp: DateTime<Utc>,
    },

    /// Job execution started
    Started {
        job_id: String,
        job_type: String,
        stone_name: Option<String>,
        timestamp: DateTime<Utc>,
    },

    /// Job progress update
    Progress {
        job_id: String,
        job_type: String,
        progress_message: String,
        stone_name: Option<String>,
        timestamp: DateTime<Utc>,
    },

    /// Job completed successfully
    Completed {
        job_id: String,
        job_type: String,
        result_message: String,
        stone_name: Option<String>,
        timestamp: DateTime<Utc>,
    },

    /// Job failed
    Failed {
        job_id: String,
        job_type: String,
        error: String,
        stone_name: Option<String>,
        timestamp: DateTime<Utc>,
    },
}

impl JobEvent {
    pub fn timestamp(&self) -> DateTime<Utc> {
        match self {
            JobEvent::Created { timestamp, .. } => *timestamp,
            JobEvent::Started { timestamp, .. } => *timestamp,
            JobEvent::Progress { timestamp, .. } => *timestamp,
            JobEvent::Completed { timestamp, .. } => *timestamp,
            JobEvent::Failed { timestamp, .. } => *timestamp,
        }
    }

    pub fn job_id(&self) -> &str {
        match self {
            JobEvent::Created { job_id, .. } => job_id,
            JobEvent::Started { job_id, .. } => job_id,
            JobEvent::Progress { job_id, .. } => job_id,
            JobEvent::Completed { job_id, .. } => job_id,
            JobEvent::Failed { job_id, .. } => job_id,
        }
    }

    pub fn stone_name(&self) -> Option<&str> {
        match self {
            JobEvent::Created { stone_name, .. } => stone_name.as_deref(),
            JobEvent::Started { stone_name, .. } => stone_name.as_deref(),
            JobEvent::Progress { stone_name, .. } => stone_name.as_deref(),
            JobEvent::Completed { stone_name, .. } => stone_name.as_deref(),
            JobEvent::Failed { stone_name, .. } => stone_name.as_deref(),
        }
    }
}

// ============================================================================
// Discovery Events
// ============================================================================

/// Stone discovery events
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_type")]
pub enum DiscoveryEvent {
    /// Stone discovered via UDP broadcast
    StoneDiscovered {
        stone_name: String,
        endpoint: String,
        moss_version: String,
        timestamp: DateTime<Utc>,
    },

    /// Stone no longer responding to discovery
    StoneLost {
        stone_name: String,
        endpoint: String,
        timestamp: DateTime<Utc>,
    },
}

impl DiscoveryEvent {
    pub fn timestamp(&self) -> DateTime<Utc> {
        match self {
            DiscoveryEvent::StoneDiscovered { timestamp, .. } => *timestamp,
            DiscoveryEvent::StoneLost { timestamp, .. } => *timestamp,
        }
    }

    pub fn stone_name(&self) -> &str {
        match self {
            DiscoveryEvent::StoneDiscovered { stone_name, .. } => stone_name,
            DiscoveryEvent::StoneLost { stone_name, .. } => stone_name,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_service_event_serialization() {
        let event = ServiceEvent::InstallStarted {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            offering: "mongodb".into(),
            timestamp: Utc::now(),
        };

        let json = serde_json::to_string(&event).unwrap();
        let deserialized: ServiceEvent = serde_json::from_str(&json).unwrap();

        assert_eq!(event.stone_name(), deserialized.stone_name());
    }

    #[test]
    fn test_domain_event_serialization() {
        let service_event = ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "postgres".into(),
            timestamp: Utc::now(),
        };

        let domain_event = DomainEvent::Service(service_event);
        let json = domain_event.to_json().unwrap();
        let deserialized: DomainEvent = serde_json::from_str(&json).unwrap();

        assert_eq!(domain_event.stone_name(), deserialized.stone_name());
    }

    #[test]
    fn test_job_event_without_stone() {
        let event = JobEvent::Created {
            job_id: "job-123".into(),
            job_type: "cleanup".into(),
            stone_name: None,
            timestamp: Utc::now(),
        };

        assert_eq!(event.job_id(), "job-123");
        assert_eq!(event.stone_name(), None);
    }
}
