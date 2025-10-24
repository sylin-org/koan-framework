using Koan.Core;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.SeedData;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.Meridian.Initialization;

/// <summary>
/// Seeds default AnalysisType templates for Meridian.
/// Follows Koan pattern: static seed data in SeedData folder, initialization via IKoanInitializer.
/// </summary>
public class AnalysisTypeSeeder : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Use background task to seed after app starts (allows Entity framework to be ready)
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500); // Wait slightly longer for app to fully start

            try
            {
                var analysisTypes = AnalysisTypeSeedData.GetAnalysisTypes();
                var seededCount = 0;
                var updatedCount = 0;

                foreach (var seedType in analysisTypes)
                {
                    var existing = await AnalysisType.Get(seedType.Id, CancellationToken.None);

                    if (existing == null)
                    {
                        // Create new analysis type
                        await seedType.Save(CancellationToken.None);
                        seededCount++;
                        Console.WriteLine($"[Meridian] Seeded AnalysisType: {seedType.Name} ({seedType.Code}) v{seedType.Version}");
                    }
                    else
                    {
                        // Check if update needed (version changed or content differs)
                        var needsUpdate = existing.Version != seedType.Version ||
                                        !AreEqual(existing, seedType);

                        if (needsUpdate)
                        {
                            // Preserve existing data, update from seed
                            existing.Name = seedType.Name;
                            existing.Code = seedType.Code;
                            existing.Description = seedType.Description;
                            existing.Version = seedType.Version;
                            existing.Tags = seedType.Tags;
                            existing.Descriptors = seedType.Descriptors;
                            existing.Instructions = seedType.Instructions;
                            existing.OutputTemplate = seedType.OutputTemplate;
                            existing.JsonSchema = seedType.JsonSchema;
                            existing.UpdatedAt = DateTime.UtcNow;

                            await existing.Save(CancellationToken.None);
                            updatedCount++;
                            Console.WriteLine($"[Meridian] Updated AnalysisType: {existing.Name} to v{existing.Version}");
                        }
                    }
                }

                if (seededCount > 0 || updatedCount > 0)
                {
                    Console.WriteLine($"[Meridian] AnalysisType seeding complete: {seededCount} created, {updatedCount} updated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Meridian] AnalysisType seed error: {ex.Message}");
            }
        });
    }

    private static bool AreEqual(AnalysisType existing, AnalysisType seed)
    {
        return existing.Name == seed.Name &&
               existing.Code == seed.Code &&
               existing.Description == seed.Description &&
               existing.Instructions == seed.Instructions &&
               existing.OutputTemplate == seed.OutputTemplate &&
               existing.JsonSchema == seed.JsonSchema &&
               SequenceEqual(existing.Tags, seed.Tags) &&
               SequenceEqual(existing.Descriptors, seed.Descriptors);
    }

    private static bool SequenceEqual(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count) return false;
        return left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }
}
