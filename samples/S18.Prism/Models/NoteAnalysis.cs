namespace S18.Prism.Models;

public sealed record NoteAnalysis
{
    public List<string> ActionItems { get; init; } = [];
    public List<string> People { get; init; } = [];
    public List<string> Organizations { get; init; } = [];
    public List<string> Questions { get; init; } = [];
    public List<string> References { get; init; } = [];
    public DateTime? AnalyzedAt { get; init; }
    public int? TokensUsed { get; init; }
}
