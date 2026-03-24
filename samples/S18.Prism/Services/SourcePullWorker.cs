using Koan.Data.Core;
using S18.Prism.Models;
using S18.Prism.Services.SourcePulling;

namespace S18.Prism.Services;

public class SourcePullWorker : BackgroundService
{
    private readonly ILogger<SourcePullWorker> _logger;
    private readonly IEnumerable<ISourcePullAdapter> _adapters;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public SourcePullWorker(
        ILogger<SourcePullWorker> logger,
        IEnumerable<ISourcePullAdapter> adapters)
    {
        _logger = logger;
        _adapters = adapters;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SourcePullWorker started with {AdapterCount} adapters: {Types}",
            _adapters.Count(),
            string.Join(", ", _adapters.Select(a => a.SupportedType)));

        // Allow framework boot to complete before querying entities
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PullDueSources(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in SourcePullWorker cycle");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("SourcePullWorker stopped");
    }

    private async Task PullDueSources(CancellationToken ct)
    {
        var sources = await Source.Query(s => s.Enabled, ct);

        foreach (var source in sources)
        {
            if (!IsDue(source))
                continue;

            var adapter = _adapters.FirstOrDefault(a => a.SupportedType == source.Type);
            if (adapter is null)
            {
                _logger.LogDebug(
                    "No adapter registered for source type {SourceType}, skipping {SourceId}",
                    source.Type, source.Id);
                continue;
            }

            _logger.LogInformation(
                "Pulling source {SourceId} ({SourceName}, type={SourceType})",
                source.Id, source.Name, source.Type);

            try
            {
                var notes = await adapter.Pull(source, ct);

                source.LastPulledAt = DateTime.UtcNow;
                source.TotalItemsPulled += notes.Count;

                // Clear immediate flag after execution (no interval = won't re-run automatically)
                if (source.Schedule.Immediate)
                    source.Schedule = new Schedule { Immediate = false };

                await source.Save(ct);

                _logger.LogInformation(
                    "Source {SourceId} pulled: {NewCount} new notes (total={Total})",
                    source.Id, notes.Count, source.TotalItemsPulled);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to pull source {SourceId} ({SourceName}, type={SourceType})",
                    source.Id, source.Name, source.Type);

                // Update timestamp anyway so we don't retry immediately
                source.LastPulledAt = DateTime.UtcNow;
                await source.Save(ct);
            }
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
