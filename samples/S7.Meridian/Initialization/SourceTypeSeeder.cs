using Koan.Core;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.SeedData;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.Meridian.Initialization;

/// <summary>
/// Seeds default SourceType classifications for Meridian.
/// Follows Koan pattern: static seed data in SeedData folder, initialization via IKoanInitializer.
/// </summary>
public class SourceTypeSeeder : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Use background task to seed after app starts (allows Entity framework to be ready)
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000); // Wait for app to fully start

            try
            {
                var sourceTypes = SourceTypeSeedData.GetSourceTypes();
                var seededCount = 0;
                var updatedCount = 0;

                foreach (var seedType in sourceTypes)
                {
                    var existing = await SourceType.Get(seedType.Id, CancellationToken.None);

                    if (existing == null)
                    {
                        // Create new
                        await seedType.Save(CancellationToken.None);
                        seededCount++;
                        Console.WriteLine($"[Meridian] Seeded SourceType: {seedType.Name} ({seedType.Code}) v{seedType.Version}");
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
                            existing.DescriptorHints = seedType.DescriptorHints;
                            existing.SignalPhrases = seedType.SignalPhrases;
                            existing.SupportsManualSelection = seedType.SupportsManualSelection;
                            existing.ExpectedPageCountMin = seedType.ExpectedPageCountMin;
                            existing.ExpectedPageCountMax = seedType.ExpectedPageCountMax;
                            existing.MimeTypes = seedType.MimeTypes;
                            existing.FieldQueries = seedType.FieldQueries;
                            existing.Instructions = seedType.Instructions;
                            existing.OutputTemplate = seedType.OutputTemplate;
                            existing.UpdatedAt = DateTime.UtcNow;

                            // Update instruction timestamps
                            if (existing.Instructions != seedType.Instructions)
                            {
                                existing.InstructionsUpdatedAt = DateTime.UtcNow;
                            }
                            if (existing.OutputTemplate != seedType.OutputTemplate)
                            {
                                existing.OutputTemplateUpdatedAt = DateTime.UtcNow;
                            }

                            // Clear embeddings when content changes
                            existing.TypeEmbedding = null;
                            existing.TypeEmbeddingVersion = 0;
                            existing.TypeEmbeddingHash = null;
                            existing.TypeEmbeddingComputedAt = null;

                            await existing.Save(CancellationToken.None);
                            updatedCount++;
                            Console.WriteLine($"[Meridian] Updated SourceType: {existing.Name} ({existing.Code}) to v{existing.Version}");
                        }
                    }
                }

                if (seededCount > 0 || updatedCount > 0)
                {
                    Console.WriteLine($"[Meridian] SourceType seeding complete: {seededCount} created, {updatedCount} updated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Meridian] SourceType seed error: {ex.Message}");
            }
        });
    }

    private static bool AreEqual(SourceType existing, SourceType seed)
    {
        return existing.Name == seed.Name &&
               existing.Code == seed.Code &&
               existing.Description == seed.Description &&
               existing.Instructions == seed.Instructions &&
               existing.OutputTemplate == seed.OutputTemplate &&
               existing.SupportsManualSelection == seed.SupportsManualSelection &&
               existing.ExpectedPageCountMin == seed.ExpectedPageCountMin &&
               existing.ExpectedPageCountMax == seed.ExpectedPageCountMax &&
               SequenceEqual(existing.Tags, seed.Tags) &&
               SequenceEqual(existing.DescriptorHints, seed.DescriptorHints) &&
               SequenceEqual(existing.SignalPhrases, seed.SignalPhrases) &&
               SequenceEqual(existing.MimeTypes, seed.MimeTypes);
    }

    private static bool SequenceEqual(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count) return false;
        return left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }
}
