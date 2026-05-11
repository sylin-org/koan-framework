namespace Koan.Jobs.RateGating;

/// <summary>
/// Thrown by a job handler when it detects an upstream rate-limit response (HTTP 429 with
/// <c>Retry-After</c>, provider-specific quota response, etc.). The <see cref="Execution.JobExecutor"/>
/// catches this exception, sets a host-rate gate via <see cref="IHostRateGate"/>, and uses
/// <see cref="RetryAfter"/> as the individual retry delay.
/// </summary>
/// <remarks>
/// <para>
/// Other jobs that target the same host short-circuit at dispatch start (see
/// <see cref="IHostRateGate"/> documentation) so they don't all consume their retry budgets while
/// upstream is unavailable.
/// </para>
/// <para>
/// Handlers don't need to call <see cref="IHostRateGate"/> directly — throw this exception and the
/// framework does the rest.
/// </para>
/// </remarks>
public sealed class RateLimitedJobException : Exception
{
    /// <summary>Host tag the rate limit applies to (e.g. <c>"nexusmods"</c>).</summary>
    public string HostTag { get; }

    /// <summary>How long to defer retries. Typically comes from the upstream's <c>Retry-After</c> header.</summary>
    public TimeSpan RetryAfter { get; }

    public RateLimitedJobException(string hostTag, TimeSpan retryAfter, string? message = null, Exception? inner = null)
        : base(message ?? FormatDefault(hostTag, retryAfter), inner)
    {
        if (string.IsNullOrWhiteSpace(hostTag))
            throw new ArgumentException("Host tag must be non-empty.", nameof(hostTag));
        if (retryAfter <= TimeSpan.Zero)
            throw new ArgumentException("Retry-after must be positive.", nameof(retryAfter));

        HostTag = hostTag;
        RetryAfter = retryAfter;
    }

    private static string FormatDefault(string hostTag, TimeSpan retryAfter)
        => $"Host '{hostTag}' rate-limited; retry after {retryAfter.TotalSeconds:F0}s.";
}
