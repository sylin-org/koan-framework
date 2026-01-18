//! Standardized Job and Retry Patterns
//! Consistent retry policies and background job handling

use std::time::Duration;
use tokio::time::sleep;

/// Retry policy configuration
#[derive(Debug, Clone)]
pub struct RetryPolicy {
    pub max_attempts: u32,
    pub base_delay: Duration,
    pub max_delay: Duration,
    pub backoff_multiplier: f32,
}

impl Default for RetryPolicy {
    fn default() -> Self {
        Self {
            max_attempts: 3,
            base_delay: Duration::from_secs(1),
            max_delay: Duration::from_secs(30),
            backoff_multiplier: 2.0,
        }
    }
}

impl RetryPolicy {
    /// Create a new retry policy
    pub fn new(max_attempts: u32, base_delay: Duration) -> Self {
        Self {
            max_attempts,
            base_delay,
            ..Default::default()
        }
    }

    /// Create a policy with no retries
    pub fn no_retry() -> Self {
        Self {
            max_attempts: 1,
            ..Default::default()
        }
    }

    /// Create a policy with exponential backoff
    pub fn exponential(max_attempts: u32, base_delay: Duration, max_delay: Duration) -> Self {
        Self {
            max_attempts,
            base_delay,
            max_delay,
            backoff_multiplier: 2.0,
        }
    }

    /// Create a policy with fixed delay
    pub fn fixed(max_attempts: u32, delay: Duration) -> Self {
        Self {
            max_attempts,
            base_delay: delay,
            max_delay: delay,
            backoff_multiplier: 1.0,
        }
    }

    /// Calculate delay for a given attempt (0-indexed)
    pub fn delay_for_attempt(&self, attempt: u32) -> Duration {
        if attempt == 0 {
            return Duration::from_secs(0);
        }

        let delay_secs = self.base_delay.as_secs_f32()
            * self.backoff_multiplier.powi((attempt - 1) as i32);
        Duration::from_secs_f32(delay_secs.min(self.max_delay.as_secs_f32()))
    }
}

/// Retry a fallible async operation according to a policy
pub async fn retry_with_policy<F, Fut, T, E>(
    policy: &RetryPolicy,
    operation: F,
) -> Result<T, E>
where
    F: Fn() -> Fut,
    Fut: std::future::Future<Output = Result<T, E>>,
{
    let mut attempt = 0;

    loop {
        attempt += 1;

        match operation().await {
            Ok(result) => return Ok(result),
            Err(err) => {
                if attempt >= policy.max_attempts {
                    return Err(err);
                }

                let delay = policy.delay_for_attempt(attempt);
                sleep(delay).await;
            }
        }
    }
}

/// Retry a fallible async operation with a simple max attempts limit
pub async fn retry_simple<F, Fut, T, E>(
    max_attempts: u32,
    delay: Duration,
    operation: F,
) -> Result<T, E>
where
    F: Fn() -> Fut,
    Fut: std::future::Future<Output = Result<T, E>>,
{
    let policy = RetryPolicy::fixed(max_attempts, delay);
    retry_with_policy(&policy, operation).await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_retry_policy_delays() {
        let policy = RetryPolicy::exponential(
            5,
            Duration::from_secs(1),
            Duration::from_secs(10),
        );

        assert_eq!(policy.delay_for_attempt(0).as_secs(), 0); // First attempt, no delay
        assert_eq!(policy.delay_for_attempt(1).as_secs(), 1); // 1s
        assert_eq!(policy.delay_for_attempt(2).as_secs(), 2); // 2s
        assert_eq!(policy.delay_for_attempt(3).as_secs(), 4); // 4s
        assert_eq!(policy.delay_for_attempt(4).as_secs(), 8); // 8s
        assert_eq!(policy.delay_for_attempt(5).as_secs(), 10); // Capped at max_delay
    }

    #[test]
    fn test_fixed_retry_policy() {
        let policy = RetryPolicy::fixed(3, Duration::from_secs(5));

        assert_eq!(policy.delay_for_attempt(0).as_secs(), 0);
        assert_eq!(policy.delay_for_attempt(1).as_secs(), 5);
        assert_eq!(policy.delay_for_attempt(2).as_secs(), 5);
        assert_eq!(policy.delay_for_attempt(3).as_secs(), 5);
    }

    #[tokio::test]
    async fn test_retry_with_policy_success() {
        use std::sync::Arc;
        use std::sync::atomic::{AtomicU32, Ordering};

        let call_count = Arc::new(AtomicU32::new(0));
        let policy = RetryPolicy::fixed(3, Duration::from_millis(10));

        let count_clone = call_count.clone();
        let result = retry_with_policy(&policy, || {
            let count = count_clone.clone();
            async move {
                let current = count.fetch_add(1, Ordering::SeqCst) + 1;
                if current < 2 {
                    Err("fail")
                } else {
                    Ok("success")
                }
            }
        })
        .await;

        assert_eq!(result, Ok("success"));
        assert_eq!(call_count.load(Ordering::SeqCst), 2);
    }

    #[tokio::test]
    async fn test_retry_with_policy_exhausted() {
        use std::sync::Arc;
        use std::sync::atomic::{AtomicU32, Ordering};

        let call_count = Arc::new(AtomicU32::new(0));
        let policy = RetryPolicy::fixed(3, Duration::from_millis(10));

        let count_clone = call_count.clone();
        let result = retry_with_policy(&policy, || {
            let count = count_clone.clone();
            async move {
                count.fetch_add(1, Ordering::SeqCst);
                Err::<(), &str>("always fail")
            }
        })
        .await;

        assert_eq!(result, Err("always fail"));
        assert_eq!(call_count.load(Ordering::SeqCst), 3);
    }
}
