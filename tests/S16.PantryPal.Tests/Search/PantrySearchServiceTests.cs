using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S16.PantryPal.Tests.Search;

[Collection("KoanHost")]
public class PantrySearchServiceTests
{
    [Fact]
    public async Task Empty_Query_Returns_FirstPage()
    {
        for (int i = 0; i < 5; i++)
            await new PantryItem { Name = $"Seed{i}", Status = "available" }.Save();

        var svc = new PantrySearchService();
        var (items, degraded) = await svc.SearchAsync(null, 10, CancellationToken.None);
        items.Should().NotBeEmpty();
        degraded.Should().BeFalse();
    }

    [Fact]
    public async Task Term_Query_Filters_By_Name()
    {
        await new PantryItem { Name = "Organic Milk", Status = "available", Category = "dairy" }.Save();
        await new PantryItem { Name = "Carrot", Status = "available", Category = "produce" }.Save();
        await new PantryItem { Name = "Almond Milk", Status = "available", Category = "dairy" }.Save();

        var svc = new PantrySearchService();
        var (items, degraded) = await svc.SearchAsync("milk", 10, CancellationToken.None);
        items.Should().OnlyContain(i => (i.Name ?? "").Contains("Milk", StringComparison.OrdinalIgnoreCase));
        items.Should().HaveCountGreaterOrEqualTo(2);
        degraded.Should().BeTrue(); // lexical fallback likely (unless vectors available)
    }

    [Fact]
    public async Task Multi_Term_Query_Matches_Either()
    {
        await new PantryItem { Name = "Green Apple", Category = "produce", Status = "available" }.Save();
        await new PantryItem { Name = "Red Onion", Category = "produce", Status = "available" }.Save();
        await new PantryItem { Name = "Apple Juice", Category = "beverage", Status = "available" }.Save();

        var svc = new PantrySearchService();
        var (items, _) = await svc.SearchAsync("apple onion", 10, CancellationToken.None);
        items.Should().NotBeEmpty();
        items.Any(i => (i.Name ?? "").Contains("Apple", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        items.Any(i => (i.Name ?? "").Contains("Onion", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }
}
