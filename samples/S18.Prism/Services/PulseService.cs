using Koan.Data.Core;
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

        // Group notes by origin and generate AI summaries
        var groups = new List<PulseGroup>();
        var notesByOrigin = recentNotes
            .GroupBy(n => n.Origin)
            .OrderByDescending(g => g.Count());

        foreach (var originGroup in notesByOrigin)
        {
            var items = originGroup
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new PulseItem(
                    NoteId: n.Id.ToString(),
                    Title: n.Title,
                    Summary: n.Summary,
                    SourceName: null,
                    Date: n.SourcePublishedAt ?? n.CreatedAt.UtcDateTime))
                .ToList();

            var summary = await GenerateGroupSummaryAsync(originGroup.Key, originGroup.ToList(), ct);

            groups.Add(new PulseGroup(
                Origin: originGroup.Key.ToString(),
                Summary: summary,
                Count: items.Count,
                Items: items));
        }

        // Build legacy briefing with enriched summary
        var overallSummary = groups.Count > 0
            ? string.Join(" ", groups.Select(g => $"{g.Origin}: {g.Summary}"))
            : $"{recentNotes.Count} new notes in the last 24 hours.";

        var briefing = new PulseBriefing
        {
            SpaceId = spaceId,
            GeneratedAt = DateTime.UtcNow,
            NewNotesCount = recentNotes.Count,
            PendingFindingsCount = pendingFindings.Count,
            TopConcepts = topConcepts,
            Summary = overallSummary,
            Groups = groups,
            TotalNewItems = recentNotes.Count
        };

        _logger.LogInformation(
            "Pulse briefing for space {SpaceId}: {NoteCount} notes across {GroupCount} groups, {FindingCount} pending findings",
            spaceId, briefing.NewNotesCount, groups.Count, briefing.PendingFindingsCount);

        return briefing;
    }

    private async Task<string> GenerateGroupSummaryAsync(
        NoteOrigin origin, List<Note> notes, CancellationToken ct)
    {
        if (notes.Count == 0)
            return "No items.";

        var prompt = $"Summarize these {notes.Count} {origin} items in one sentence: " +
                     string.Join("; ", notes.Select(n => n.Title ?? n.Summary ?? "untitled"));

        try
        {
            var summary = await Koan.AI.Client.Chat(prompt, ct);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI summarization failed for {Origin} group, using fallback", origin);
            return $"{notes.Count} {origin.ToString().ToLowerInvariant()} items including: " +
                   string.Join(", ", notes.Take(3).Select(n => n.Title ?? "untitled"));
        }
    }
}
