//! Event bus for domain event distribution
//!
//! Simplified design: Single event type (DomainEvent), multiple subscribers.
//! Subscribers receive all events, can filter by category in handler.

use super::DomainEvent;
use std::sync::Arc;
use tokio::sync::{broadcast, RwLock};

/// Event handler callback type
pub type EventHandler = Arc<dyn Fn(DomainEvent) + Send + Sync>;

/// Centralized event bus
///
/// Features:
/// - Broadcast channel for fan-out to multiple subscribers
/// - Non-blocking publishes (dropped if no capacity)
/// - Typed subscriptions via DomainEvent enum
///
/// Usage:
/// ```ignore
/// let bus = EventBus::new(1000);
///
/// // Subscribe to events
/// let mut rx = bus.subscribe();
/// tokio::spawn(async move {
///     while let Ok(event) = rx.recv().await {
///         match event {
///             DomainEvent::Service(service_event) => {
///                 println!("Service event: {:?}", service_event);
///             }
///             _ => {}
///         }
///     }
/// });
///
/// // Publish events
/// bus.publish(DomainEvent::Service(ServiceEvent::Started { ... })).await?;
/// ```
#[derive(Clone)]
pub struct EventBus {
    tx: broadcast::Sender<DomainEvent>,
    handlers: Arc<RwLock<Vec<EventHandler>>>,
}

impl EventBus {
    /// Create a new event bus with given channel capacity
    ///
    /// Capacity determines how many events can be buffered per subscriber.
    /// Higher capacity reduces message loss under high load.
    pub fn new(capacity: usize) -> Self {
        let (tx, _rx) = broadcast::channel(capacity);
        Self {
            tx,
            handlers: Arc::new(RwLock::new(Vec::new())),
        }
    }

    /// Subscribe to all events
    ///
    /// Returns a receiver that gets a copy of every published event.
    /// Filter events in your handler logic.
    pub fn subscribe(&self) -> broadcast::Receiver<DomainEvent> {
        self.tx.subscribe()
    }

    /// Publish an event to all subscribers
    ///
    /// Non-blocking: If channel is full, event may be dropped.
    /// Returns Ok(subscriber_count) on success.
    pub async fn publish(&self, event: DomainEvent) -> Result<usize, EventBusError> {
        // First, notify registered handlers
        let handlers = self.handlers.read().await;
        let handler_count = handlers.len();
        for handler in handlers.iter() {
            handler(event.clone());
        }
        drop(handlers); // Release lock before broadcast

        // Then broadcast to subscribers
        match self.tx.send(event) {
            Ok(subscriber_count) => Ok(subscriber_count),
            Err(_) => {
                // If no subscribers but handlers exist, that's still ok
                if handler_count > 0 {
                    Ok(0)
                } else {
                    Err(EventBusError::NoSubscribers)
                }
            }
        }
    }

    /// Register a synchronous event handler
    ///
    /// Handlers are called inline during publish (non-blocking).
    /// Use for lightweight operations like logging.
    /// For heavy work, spawn a task in the handler.
    pub async fn register_handler<F>(&self, handler: F)
    where
        F: Fn(DomainEvent) + Send + Sync + 'static,
    {
        let mut handlers = self.handlers.write().await;
        handlers.push(Arc::new(handler));
    }

    /// Get current subscriber count
    pub fn subscriber_count(&self) -> usize {
        self.tx.receiver_count()
    }
}

/// Event bus errors
#[derive(Debug, thiserror::Error)]
pub enum EventBusError {
    #[error("No active subscribers")]
    NoSubscribers,
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::events::ServiceEvent;
    use chrono::Utc;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use tokio::time::{timeout, Duration};

    #[tokio::test]
    async fn test_event_bus_publish_subscribe() {
        let bus = EventBus::new(10);
        let mut rx = bus.subscribe();

        let event = DomainEvent::Service(ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            timestamp: Utc::now(),
        });

        bus.publish(event.clone()).await.unwrap();

        let received = timeout(Duration::from_secs(1), rx.recv())
            .await
            .expect("timeout")
            .expect("recv failed");

        assert_eq!(received.stone_name(), event.stone_name());
    }

    #[tokio::test]
    async fn test_event_bus_multiple_subscribers() {
        let bus = EventBus::new(10);
        let mut rx1 = bus.subscribe();
        let mut rx2 = bus.subscribe();

        assert_eq!(bus.subscriber_count(), 2);

        let event = DomainEvent::Service(ServiceEvent::Stopped {
            stone_name: "stone-01".into(),
            service_name: "postgres".into(),
            timestamp: Utc::now(),
        });

        bus.publish(event.clone()).await.unwrap();

        let received1 = timeout(Duration::from_secs(1), rx1.recv())
            .await
            .expect("timeout")
            .expect("recv failed");
        let received2 = timeout(Duration::from_secs(1), rx2.recv())
            .await
            .expect("timeout")
            .expect("recv failed");

        assert_eq!(received1.stone_name(), event.stone_name());
        assert_eq!(received2.stone_name(), event.stone_name());
    }

    #[tokio::test]
    async fn test_event_bus_handler_registration() {
        let bus = EventBus::new(10);
        let counter = Arc::new(AtomicUsize::new(0));

        let counter_clone = counter.clone();
        bus.register_handler(move |_event| {
            counter_clone.fetch_add(1, Ordering::SeqCst);
        })
        .await;

        let event = DomainEvent::Service(ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            timestamp: Utc::now(),
        });

        bus.publish(event).await.unwrap();

        // Give handler time to execute (increased for Windows reliability)
        tokio::time::sleep(Duration::from_millis(200)).await;

        assert_eq!(counter.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn test_event_bus_no_subscribers() {
        let bus = EventBus::new(10);

        let event = DomainEvent::Service(ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            timestamp: Utc::now(),
        });

        // Should fail with NoSubscribers (no handlers, no subscribers)
        let result = bus.publish(event).await;
        assert!(result.is_err());
    }

    #[tokio::test]
    async fn test_event_filtering() {
        let bus = EventBus::new(10);
        let mut rx = bus.subscribe();

        let service_counter = Arc::new(AtomicUsize::new(0));
        let job_counter = Arc::new(AtomicUsize::new(0));

        let service_clone = service_counter.clone();
        let job_clone = job_counter.clone();

        tokio::spawn(async move {
            while let Ok(event) = rx.recv().await {
                match event {
                    DomainEvent::Service(_) => {
                        service_clone.fetch_add(1, Ordering::SeqCst);
                    }
                    DomainEvent::Job(_) => {
                        job_clone.fetch_add(1, Ordering::SeqCst);
                    }
                    _ => {}
                }
            }
        });

        // Publish mixed events
        bus.publish(DomainEvent::Service(ServiceEvent::Started {
            stone_name: "stone-01".into(),
            service_name: "mongodb".into(),
            timestamp: Utc::now(),
        }))
        .await
        .unwrap();

        bus.publish(DomainEvent::Job(crate::events::JobEvent::Created {
            job_id: "job-123".into(),
            job_type: "install".into(),
            stone_name: Some("stone-01".into()),
            timestamp: Utc::now(),
        }))
        .await
        .unwrap();

        bus.publish(DomainEvent::Service(ServiceEvent::Stopped {
            stone_name: "stone-01".into(),
            service_name: "postgres".into(),
            timestamp: Utc::now(),
        }))
        .await
        .unwrap();

        // Give handlers time to process
        tokio::time::sleep(Duration::from_millis(100)).await;

        assert_eq!(service_counter.load(Ordering::SeqCst), 2);
        assert_eq!(job_counter.load(Ordering::SeqCst), 1);
    }
}
