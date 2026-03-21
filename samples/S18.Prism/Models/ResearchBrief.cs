using Koan.Data.Core.Model;

namespace S18.Prism.Models;

public class ResearchBrief : Entity<ResearchBrief>
{
    public string Name { get; set; } = "";
    public string SpaceId { get; set; } = "";
    public string Topic { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public List<string> Exclusions { get; set; } = [];
    public SearchScope Scope { get; set; } = new();
    public Schedule Schedule { get; set; } = Schedule.Daily;
    public IngestPolicy Policy { get; set; } = IngestPolicy.Adaptive;
    public int MaxItemsPerRun { get; set; } = 20;
    public double RelevanceThreshold { get; set; } = 0.7;

    public int TotalItemsFound { get; set; }
    public int TotalItemsIngested { get; set; }
    public int TotalItemsDismissed { get; set; }
    public DateTime? LastRunAt { get; set; }
}

public sealed record SearchScope
{
    public bool Web { get; init; } = true;
    public bool Arxiv { get; init; }
    public bool HackerNews { get; init; }
    public bool Reddit { get; init; }
    public bool YouTube { get; init; }
    public bool GitHub { get; init; }
    public bool News { get; init; }
    public List<string> CustomUrls { get; init; } = [];

    public static SearchScope All => new()
    {
        Web = true, Arxiv = true, HackerNews = true,
        Reddit = true, YouTube = true, GitHub = true, News = true
    };
    public static SearchScope Academic => new() { Arxiv = true, Web = true };
    public static SearchScope Tech => new() { HackerNews = true, GitHub = true, Web = true, YouTube = true };
}

public enum IngestPolicy
{
    AutoIngest,
    ReviewFirst,
    Digest,
    Adaptive
}
