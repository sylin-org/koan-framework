using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services;

public class SourcePullWorker : BackgroundService
{
    private readonly ILogger<SourcePullWorker> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public SourcePullWorker(ILogger<SourcePullWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SourcePullWorker started");

        // Allow framework boot to complete before querying entities
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PullDueSourcesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in SourcePullWorker cycle");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("SourcePullWorker stopped");
    }

    private async Task PullDueSourcesAsync(CancellationToken ct)
    {
        var sources = await Source.Query(s => s.Enabled, ct);

        foreach (var source in sources)
        {
            if (!IsDue(source))
                continue;

            _logger.LogInformation(
                "Pulling source {SourceId} ({SourceName}, type={SourceType})",
                source.Id, source.Name, source.Type);

            // Actual pull logic is deferred to per-type adapters (future implementation)
            // For now, update the last-pulled timestamp
            source.LastPulledAt = DateTime.UtcNow;
            await source.Save(ct);

            _logger.LogDebug("Source {SourceId} marked as pulled at {PulledAt}",
                source.Id, source.LastPulledAt);
        }
    }

    private static bool IsDue(Source source)
    {
        if (source.LastPulledAt is null)
            return true;

        if (source.Schedule.Immediate)
            return true;

        if (source.Schedule.Interval is null)
            return false;

        return DateTime.UtcNow - source.LastPulledAt.Value >= source.Schedule.Interval.Value;
    }
}
