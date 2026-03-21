using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services;

public class ResearchBriefWorker : BackgroundService
{
    private readonly ILogger<ResearchBriefWorker> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    public ResearchBriefWorker(ILogger<ResearchBriefWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ResearchBriefWorker started");

        // Allow framework boot to complete before querying entities
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteDueBriefsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in ResearchBriefWorker cycle");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("ResearchBriefWorker stopped");
    }

    private async Task ExecuteDueBriefsAsync(CancellationToken ct)
    {
        var briefs = await ResearchBrief.Query(_ => true, ct);

        foreach (var brief in briefs)
        {
            if (!IsDue(brief))
                continue;

            _logger.LogInformation(
                "Executing research brief {BriefId} ({BriefName}) for topic: {Topic}",
                brief.Id, brief.Name, brief.Topic);

            // Actual research execution is deferred to AI-powered search (future implementation)
            // For now, update the last-run timestamp
            brief.LastRunAt = DateTime.UtcNow;
            await brief.Save(ct);

            _logger.LogDebug("Research brief {BriefId} marked as executed at {RunAt}",
                brief.Id, brief.LastRunAt);
        }
    }

    private static bool IsDue(ResearchBrief brief)
    {
        if (brief.LastRunAt is null)
            return true;

        if (brief.Schedule.Immediate)
            return true;

        if (brief.Schedule.Interval is null)
            return false;

        return DateTime.UtcNow - brief.LastRunAt.Value >= brief.Schedule.Interval.Value;
    }
}
