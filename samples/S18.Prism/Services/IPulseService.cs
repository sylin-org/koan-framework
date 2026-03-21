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
}
