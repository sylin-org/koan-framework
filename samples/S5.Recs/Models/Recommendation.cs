namespace S5.Recs.Models;

public sealed class Recommendation
{
    public required Anime Anime { get; init; }
    public double Score { get; init; }
    public string[] Reasons { get; init; } = [];
}