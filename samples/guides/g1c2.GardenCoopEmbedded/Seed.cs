using Koan.Data.Core;

namespace GardenCoopEmbedded;

/// <summary>
/// Seeds a handful of produce listings on first run. <c>[Embedding]</c> on <see cref="Produce"/> turns a plain
/// <c>Save()</c> into the whole embed→store loop: the listing is embedded in-process by the local ONNX model and
/// the vector is written to the durable sqlite-vec store automatically — no explicit embed or vector call here.
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
            await item.Save(); // [Embedding] hook embeds (local ONNX) + stores the vector (sqlite-vec) on Save
    }
}
