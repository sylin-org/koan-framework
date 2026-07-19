using Koan.Data.Core.Model;

namespace AnimeRecommendations.Domain;

/// <summary>A person whose ratings become recommendation intent.</summary>
public sealed class Viewer : Entity<Viewer>
{
    public string Name { get; set; } = "";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
