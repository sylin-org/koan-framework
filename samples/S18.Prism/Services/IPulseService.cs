namespace S18.Prism.Services;

public interface IPulseService
{
    Task<PulseBriefing> GenerateAsync(string spaceId, CancellationToken ct = default);
}

public sealed record PulseBriefing
{
    public string SpaceId { get; init; } = "";
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public int NewNotesCount { get; init; }
    public int PendingFindingsCount { get; init; }
    public List<string> TopConcepts { get; init; } = [];
    public string? Summary { get; init; }

    // Structured response with AI-generated group summaries
    public List<PulseGroup> Groups { get; init; } = [];
    public int TotalNewItems { get; init; }
}

public sealed record PulseGroup(
    string Origin,
    string Summary,
    int Count,
    List<PulseItem> Items);

public sealed record PulseItem(
    string NoteId,
    string? Title,
    string? Summary,
    string? SourceName,
    DateTime? Date);
