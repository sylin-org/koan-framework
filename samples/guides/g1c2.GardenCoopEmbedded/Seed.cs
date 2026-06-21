using Koan.AI;
using Koan.Data.Core;
using Koan.Data.Vector;

namespace GardenCoopEmbedded;

/// <summary>
/// Seeds a handful of produce listings on first run. For each, the description is embedded in-process by the
/// local ONNX model (<see cref="Client.Embed(string, System.Threading.CancellationToken)"/>) and the vector is
/// written to the durable sqlite-vec store — the whole embed→store loop runs inside this one process.
/// </summary>
internal static class Seed
{
    public static async Task EnsureAsync()
    {
        var existing = await Produce.All();
        if (existing.Count > 0) return;

        var items = new[]
        {
            new Produce { Name = "Heirloom Tomatoes", Category = "Vegetables",   Description = "Ripe red beefsteak tomatoes, vine-grown and sun-warmed." },
            new Produce { Name = "Crisp Lettuce",     Category = "Vegetables",   Description = "Fresh green romaine and butterhead heads, harvested at dawn." },
            new Produce { Name = "Wild Blueberries",  Category = "Fruit",        Description = "Tart-sweet blue berries picked from the hedgerow." },
            new Produce { Name = "Free-range Eggs",   Category = "Dairy & Eggs", Description = "Pasture-raised hen eggs with deep orange yolks." },
            new Produce { Name = "Raw Honey",         Category = "Pantry",       Description = "Unfiltered wildflower honey from the co-op's own hives." },
        };

        foreach (var item in items)
        {
            await item.Save();                                                   // row -> SQLite
            var vector = await Client.Embed($"{item.Name}. {item.Description}"); // text -> vector (local ONNX, via the AI facade)
            await Vector<Produce>.Save(item, vector);                            // vector -> sqlite-vec
        }
    }
}
