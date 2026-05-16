using System.Collections.Concurrent;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Health;

/// <summary>
/// Background service that monitors health of AI source members.
/// Updates <see cref="MemberHealthState"/> based on periodic connectivity probes
/// and implements a per-member circuit breaker pattern using <see cref="CircuitBreakerConfig"/>.
/// </summary>
internal sealed class AiSourceHealthMonitor(
    IAiSourceRegistry sourceRegistry,
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> options,
    ILogger<AiSourceHealthMonitor> logger) : BackgroundService
{
    private static readonly CircuitBreakerConfig DefaultCircuitBreaker = new();
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, MemberCircuitState> _circuits = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow startup to complete before first probe cycle.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var interval = TimeSpan.FromSeconds(
            options.Value.HealthProbeIntervalSeconds > 0
                ? options.Value.HealthProbeIntervalSeconds
                : 30);

        logger.LogInformation("AI health monitor started (interval={Interval}s)", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProbeAllMembers(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error during AI health probe cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ProbeAllMembers(CancellationToken ct)
    {
        var sources = sourceRegistry.GetAllSources();
        if (sources.Count == 0) return;

        foreach (var source in sources)
        {
            var cbConfig = source.CircuitBreaker ?? DefaultCircuitBreaker;

            foreach (var member in source.Members)
            {
                await ProbeMember(member, cbConfig, ct);
            }
        }
    }

    private async Task ProbeMember(AiMemberDefinition member, CircuitBreakerConfig cbConfig, CancellationToken ct)
    {
        var circuit = _circuits.GetOrAdd(member.Name, _ => new MemberCircuitState());
        var now = DateTimeOffset.UtcNow;

        // If in break period, skip probe — stay Unhealthy.
        if (circuit.BreakUntilUtc is { } breakUntil && now < breakUntil)
        {
            return;
        }

        // If break period just expired, transition to Recovering (half-open).
        if (circuit.BreakUntilUtc is not null && now >= circuit.BreakUntilUtc)
        {
            TransitionState(member, MemberHealthState.Recovering);
            circuit.BreakUntilUtc = null;
            circuit.ConsecutiveSuccesses = 0;
        }

        var success = await SendProbe(member.ConnectionString, ct);

        if (success)
        {
            circuit.ConsecutiveFailures = 0;
            circuit.ConsecutiveSuccesses++;

            if (circuit.ConsecutiveSuccesses >= cbConfig.SuccessThreshold
                && member.HealthState is not MemberHealthState.Healthy)
            {
                TransitionState(member, MemberHealthState.Healthy);
            }
        }
        else
        {
            circuit.ConsecutiveSuccesses = 0;
            circuit.ConsecutiveFailures++;

            if (circuit.ConsecutiveFailures >= cbConfig.FailureThreshold
                && member.HealthState is not MemberHealthState.Unhealthy)
            {
                TransitionState(member, MemberHealthState.Unhealthy);
                circuit.BreakUntilUtc = now.AddSeconds(cbConfig.BreakDurationSeconds);
                logger.LogWarning(
                    "AI member {Member} circuit opened — break until {BreakUntil}",
                    member.Name, circuit.BreakUntilUtc);
            }
        }

        circuit.LastProbeUtc = now;
    }

    private async Task<bool> SendProbe(string connectionString, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            var client = httpClientFactory.CreateClient("KoanAiHealthProbe");
            using var request = new HttpRequestMessage(HttpMethod.Head, connectionString);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            // Any HTTP response (even 4xx) means the endpoint is reachable.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TransitionState(AiMemberDefinition member, MemberHealthState newState)
    {
        var previous = member.HealthState;
        member.HealthState = newState;

        if (previous != newState)
        {
            logger.LogWarning(
                "AI member {Member} health: {Previous} -> {New}",
                member.Name, previous, newState);
        }
    }

    /// <summary>
    /// Per-member circuit breaker tracking state.
    /// </summary>
    private sealed class MemberCircuitState
    {
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveSuccesses { get; set; }
        public DateTimeOffset? LastProbeUtc { get; set; }
        public DateTimeOffset? BreakUntilUtc { get; set; }
    }
}
