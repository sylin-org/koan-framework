using System;
using Koan.Jobs.Execution;

namespace Koan.Jobs.Core.Tests.Specs.Execution;

public class CustomRetryPolicySpec
{
    [Fact(DisplayName = "CustomRetryPolicy: interface shape has ShouldRetry and ComputeDelay")]
    public void Custom_retry_policy_controls_retry_decision()
    {
        // Arrange — a policy that retries up to 3 times with linear backoff
        ICustomRetryPolicy policy = new LinearRetryPolicy(maxAttempts: 3);

        // Act & Assert — within limit
        policy.ShouldRetry(1, new InvalidOperationException("fail")).Should().BeTrue();
        policy.ShouldRetry(3, new InvalidOperationException("fail")).Should().BeTrue();

        // Act & Assert — beyond limit
        policy.ShouldRetry(4, new InvalidOperationException("fail")).Should().BeFalse();
    }

    [Fact(DisplayName = "CustomRetryPolicy: ComputeDelay returns expected backoff")]
    public void Compute_delay_returns_expected_backoff()
    {
        ICustomRetryPolicy policy = new LinearRetryPolicy(maxAttempts: 5);

        policy.ComputeDelay(1, new Exception()).Should().Be(TimeSpan.FromSeconds(1));
        policy.ComputeDelay(3, new Exception()).Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "CustomRetryPolicy: policy can reject specific exception types")]
    public void Policy_can_reject_specific_exception_types()
    {
        ICustomRetryPolicy policy = new NonRetryableExceptionPolicy();

        policy.ShouldRetry(1, new InvalidOperationException("transient")).Should().BeTrue();
        policy.ShouldRetry(1, new ArgumentException("permanent")).Should().BeFalse();
    }

    /// <summary>
    /// Test implementation: retries up to maxAttempts with linear 1s-per-attempt delay.
    /// </summary>
    private sealed class LinearRetryPolicy(int maxAttempts) : ICustomRetryPolicy
    {
        public bool ShouldRetry(int attemptNumber, Exception error) => attemptNumber <= maxAttempts;
        public TimeSpan ComputeDelay(int attemptNumber, Exception error) => TimeSpan.FromSeconds(attemptNumber);
    }

    /// <summary>
    /// Test implementation: refuses to retry ArgumentExceptions.
    /// </summary>
    private sealed class NonRetryableExceptionPolicy : ICustomRetryPolicy
    {
        public bool ShouldRetry(int attemptNumber, Exception error) => error is not ArgumentException;
        public TimeSpan ComputeDelay(int attemptNumber, Exception error) => TimeSpan.FromSeconds(1);
    }
}
