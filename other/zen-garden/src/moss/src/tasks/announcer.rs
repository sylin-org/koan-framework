//! Periodic announcement task
//!
//! Runs in background, announcing stone presence every 30 seconds.
//! Uses change detection to minimize unnecessary network traffic.
//!
//! Design:
//! - Simple interval loop (KISS)
//! - No complex state management (YAGNI)
//! - Delegates to announcement module (SoC)

use tokio::time::{interval, Duration, Instant};
use crate::AppState;

/// Start periodic announcement task
///
/// Announces stone presence every 30 seconds via all channels.
/// Uses change detection to skip announcements when state unchanged.
/// Forces announcement every 5 minutes as keep-alive.
///
/// Runs for process lifetime.
pub fn start_periodic_announcer(state: AppState) {
    tokio::spawn(async move {
        let mut ticker = interval(Duration::from_secs(30));
        ticker.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);

        let mut last_hash: Option<u64> = None;
        let mut last_announcement = Instant::now();

        // Skip first tick (already announced at startup)
        ticker.tick().await;

        loop {
            ticker.tick().await;

            let payload = crate::announcement::build_payload(&state).await;
            
            match crate::announcement::announce_if_changed(
                payload, 
                &mut last_hash, 
                &mut last_announcement,
                false, // Not forced
            ).await {
                Ok(true) => tracing::debug!("Periodic announcement sent (state changed or keep-alive)"),
                Ok(false) => tracing::trace!("Periodic announcement skipped (no changes)"),
                Err(e) => tracing::warn!(error = ?e, "Periodic announcement failed"),
            }
        }
    });

    tracing::info!("Periodic announcer started (30s interval, 5min keep-alive)");
}
