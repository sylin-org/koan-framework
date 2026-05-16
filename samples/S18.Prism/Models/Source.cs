using Koan.Data.Core.Model;

namespace S18.Prism.Models;

public class Source : Entity<Source>
{
    public string Name { get; set; } = "";
    public SourceType Type { get; set; }
    public string Configuration { get; set; } = "{}";
    public string SpaceId { get; set; } = "";
    public Schedule Schedule { get; set; } = Schedule.Daily;
    public bool Enabled { get; set; } = true;
    public DateTime? LastPulledAt { get; set; }
    public int TotalItemsPulled { get; set; }
}

public enum SourceType
{
    Rss,
    YouTube,
    Podcast,
    GitHub,
    HackerNews,
    Reddit,
    Bookmark,
    Email,
    FolderWatch,
    Web
}

public sealed record Schedule
{
    public TimeSpan? Interval { get; init; }
    public bool Immediate { get; init; }

    public static Schedule Hourly => new() { Interval = TimeSpan.FromHours(1) };
    public static Schedule Daily => new() { Interval = TimeSpan.FromDays(1) };
    public static Schedule Weekly => new() { Interval = TimeSpan.FromDays(7) };
    public static Schedule OnAdd => new() { Immediate = true };
    public static Schedule Every(TimeSpan interval) => new() { Interval = interval };
}
