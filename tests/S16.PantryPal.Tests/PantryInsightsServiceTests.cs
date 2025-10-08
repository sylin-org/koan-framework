using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;

namespace S16.PantryPal.Tests;

public class PantryInsightsServiceTests
{
    [Fact]
    public async Task Stats_ShouldAggregateCounts()
    {
        await new PantryItem { Name = "Milk", Category = "dairy", ExpiresAt = DateTime.UtcNow.AddDays(3), Status = "available" }.Save();
        await new PantryItem { Name = "Carrots", Category = "produce", ExpiresAt = DateTime.UtcNow.AddDays(10), Status = "available" }.Save();
        await new PantryItem { Name = "Old Bread", Category = "bakery", ExpiresAt = DateTime.UtcNow.AddDays(-1), Status = "available" }.Save();

        var svc = new PantryInsightsService();
        var stats = await svc.GetStatsAsync();

        stats.TotalItems.Should().BeGreaterOrEqualTo(3);
        stats.Expired.Should().BeGreaterOrEqualTo(1);
        stats.ByCategory.Select(c => c.Category).Should().Contain(new []{"dairy","produce","bakery"});
    }
}
