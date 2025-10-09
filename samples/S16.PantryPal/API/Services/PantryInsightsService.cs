using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

public interface IPantryInsightsService
{
    Task<PantryStats> GetStatsAsync(CancellationToken ct = default);
}

public sealed class PantryInsightsService : IPantryInsightsService
{
    public async Task<PantryStats> GetStatsAsync(CancellationToken ct = default)
    {
        var items = (await PantryItem.All()).ToList();
        var now = DateTime.UtcNow;
        var week = now.AddDays(7);
        var month = now.AddDays(30);

        var stats = new PantryStats
        {
            TotalItems = items.Count,
            ExpiringInWeek = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= week),
            ExpiringInMonth = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= month),
            Expired = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= now),
            ByCategory = items
                .GroupBy(i => i.Category)
                .Select(g => new CategoryCount(g.Key, g.Count()))
                .OrderByDescending(c => c.Count)
                .ToArray()
        };

        return stats;
    }
}

public sealed class PantryStats
{
    public int TotalItems { get; init; }
    public int ExpiringInWeek { get; init; }
    public int ExpiringInMonth { get; init; }
    public int Expired { get; init; }
    public CategoryCount[] ByCategory { get; init; } = Array.Empty<CategoryCount>();
}

public record CategoryCount(string Category, int Count);
