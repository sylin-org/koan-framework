using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Initialization;

public static class SpaceSeeder
{
    public static async Task SeedDefaultsAsync(ILogger logger)
    {
        logger.LogInformation("Seeding default spaces...");

        var existing = await Space.All();
        if (existing.Any())
        {
            logger.LogInformation("Spaces already seeded ({Count} spaces found)", existing.Count);
            return;
        }

        var defaultSpace = new Space
        {
            Name = "Personal",
            Description = "Your private knowledge base",
            Access = SpaceAccess.Private
        };

        await defaultSpace.Save();
        logger.LogInformation("Seeded default space: {SpaceName} ({SpaceId})",
            defaultSpace.Name, defaultSpace.Id);
    }
}
