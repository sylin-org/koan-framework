using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using Koan.Data.Core.Model;

namespace S16.PantryPal.Tests;

[Collection("KoanHost")]
public class PantryInsightsServiceTests
{
    [Fact]
    public async Task Stats_ShouldAggregateCounts()
    {
    var milk = new PantryItem { Name = "Milk", Category = "dairy", ExpiresAt = DateTime.UtcNow.AddDays(3), Status = "available" };
    await milk.Save();
    var carrots = new PantryItem { Name = "Carrots", Category = "produce", ExpiresAt = DateTime.UtcNow.AddDays(10), Status = "available" };
    await carrots.Save();
    var oldBread = new PantryItem { Name = "Old Bread", Category = "bakery", ExpiresAt = DateTime.UtcNow.AddDays(-1), Status = "available" };
    await oldBread.Save();

        var svc = new PantryInsightsService();
        var stats = await svc.GetStatsAsync();

        stats.TotalItems.Should().BeGreaterOrEqualTo(3);
        stats.Expired.Should().BeGreaterOrEqualTo(1);
        stats.ByCategory.Select(c => c.Category).Should().Contain(new []{"dairy","produce","bakery"});
    }
}
