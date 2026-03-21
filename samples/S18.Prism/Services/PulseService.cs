using S18.Prism.Models;

namespace S18.Prism.Services;

public class PulseService : IPulseService
{
    private readonly ILogger<PulseService> _logger;

    public PulseService(ILogger<PulseService> logger)
    {
        _logger = logger;
    }

    public async Task<PulseBriefing> GenerateAsync(string spaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating pulse briefing for space {SpaceId}", spaceId);

        var since = DateTime.UtcNow.AddHours(-24);

        // Query recent notes in this space
        var recentNotes = await Note.Query(
            n => n.SpaceId == spaceId && n.CreatedAt >= since, ct);

        // Query pending findings
        var pendingFindings = await ResearchFinding.Query(
            f => f.SpaceId == spaceId && f.Status == FindingStatus.PendingReview, ct);

        // Aggregate top concepts from recent notes
        var topConcepts = recentNotes
            .SelectMany(n => n.KeyConcepts)
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        var briefing = new PulseBriefing
        {
            SpaceId = spaceId,
            GeneratedAt = DateTime.UtcNow,
            NewNotesCount = recentNotes.Count,
            PendingFindingsCount = pendingFindings.Count,
            TopConcepts = topConcepts,
            Summary = $"{recentNotes.Count} new notes in the last 24 hours. " +
                      $"{pendingFindings.Count} findings awaiting review."
        };

        _logger.LogInformation(
            "Pulse briefing for space {SpaceId}: {NoteCount} notes, {FindingCount} pending findings",
            spaceId, briefing.NewNotesCount, briefing.PendingFindingsCount);

        return briefing;
    }
}
