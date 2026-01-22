//! Event system for Zen Garden
//!
//! Centralized event bus with domain events for:
//! - Service lifecycle (install, start, stop, remove)
//! - Registry updates (stone online/offline, service registered)
//! - Job progress (created, running, completed, failed)
//! - Discovery (stone found, stone lost)
//!
//! Events enable SSE streaming, background workers, and audit logging.

pub mod bus;
pub mod domain_events;

pub use bus::EventBus;
pub use domain_events::{
    DomainEvent,
    ServiceEvent,
    RegistryEvent,
    JobEvent,
    DiscoveryEvent,
};
