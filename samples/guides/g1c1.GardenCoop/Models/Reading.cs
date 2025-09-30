using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.Linq;

namespace g1c1.GardenCoop.Models;

public class Reading : Entity<Reading>
{
    [Parent(typeof(Plot))]
    public string PlotId { get; set; } = string.Empty;

    public double Moisture { get; set; }

    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;

    public static async Task<Reading[]> Recent(string plotId, int take = 20, CancellationToken ct = default)
    {
        var items = await Reading.Query(r => r.PlotId == plotId, ct);
        return items
            .OrderByDescending(r => r.SampledAt)
            .Take(take)
            .ToArray();
    }
}
