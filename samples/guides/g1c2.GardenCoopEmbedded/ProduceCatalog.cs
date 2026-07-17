using Koan.Data.Core;
using Microsoft.Extensions.Logging;

namespace GardenCoopEmbedded;

/// <summary>
/// Ensures the five starter listings on first run. <c>[Embedding]</c> on <see cref="Produce"/> turns their normal
/// <c>Save()</c> into the local embed-and-index operation; this business catalog does not name either provider.
/// </summary>
internal static class ProduceCatalog
{
    public static async Task EnsureStarterListings(ILogger logger, CancellationToken ct = default)
    {
        var existing = await Produce.All(ct);
        if (existing.Count > 0) return;

        Produce[] listings =
        {
            new() { Id = "heirloom-tomatoes", Name = "Heirloom Tomatoes", Category = "Vegetables",   Description = "Ripe red beefsteak tomatoes, vine-grown and sun-warmed." },
            new() { Id = "crisp-lettuce",     Name = "Crisp Lettuce",     Category = "Vegetables",   Description = "Fresh green romaine and butterhead heads, harvested at dawn." },
            new() { Id = "wild-blueberries",  Name = "Wild Blueberries",  Category = "Fruit",        Description = "Tart-sweet blue berries picked from the hedgerow." },
            new() { Id = "free-range-eggs",   Name = "Free-range Eggs",   Category = "Dairy & Eggs", Description = "Pasture-raised hen eggs with deep orange yolks." },
            new() { Id = "raw-honey",         Name = "Raw Honey",         Category = "Pantry",       Description = "Unfiltered wildflower honey from the co-op's own hives." },
        };

        logger.LogInformation("Planting {Count} starter produce listings.", listings.Length);
        await listings.Save(ct);
    }
}
