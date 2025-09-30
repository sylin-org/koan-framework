using g1c1.GardenCoop.Models;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace g1c1.GardenCoop.Infrastructure;

public static class GardenSeeder
{
    public static async Task EnsureSampleDataAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var ct = cancellationToken;

        if (await Plot.Count(ct) > 0)
        {
            return;
        }

        logger.LogInformation("Planting starter plots for the Garden Cooperative sample.");

        var riley = await new Member { DisplayName = "Riley" }.Save(ct);
        var mara = await new Member { DisplayName = "Mara" }.Save(ct);
        var devon = await new Member { DisplayName = "Devon" }.Save(ct);

        var bed1 = await new Plot { Name = "Bed 1", MemberId = riley.Id }.Save(ct);
        var bed2 = await new Plot { Name = "Bed 2", MemberId = mara.Id }.Save(ct);
        var bed3 = await new Plot { Name = "Bed 3", MemberId = devon.Id }.Save(ct);

        // Give the journal something to read day one.
        await new Reading
        {
            PlotId = bed1.Id,
            Moisture = 28.5,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-4)
        }.Save(ct);

        await new Reading
        {
            PlotId = bed3.Id,
            Moisture = 18.0,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-2)
        }.Save(ct);

        await new Reading
        {
            PlotId = bed3.Id,
            Moisture = 17.2,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-1)
        }.Save(ct);

        logger.LogInformation("Starter readings inserted – Bed 3 will nudge the team right away.");
    }
}
