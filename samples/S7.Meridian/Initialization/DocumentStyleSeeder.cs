using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.SeedData;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.Meridian.Initialization;

/// <summary>
/// Seeds default DocumentStyle definitions for Meridian.
/// Follows Koan pattern: static seed data in SeedData folder, initialization via IKoanInitializer.
/// </summary>
public class DocumentStyleSeeder : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Use background task to seed after app starts (allows Entity framework to be ready)
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500); // Wait for app to fully start

            try
            {
                var documentStyles = DocumentStyleSeedData.GetDocumentStyles();
                var seededCount = 0;
                var updatedCount = 0;

                foreach (var seedStyle in documentStyles)
                {
                    var existing = await DocumentStyle.Get(seedStyle.Id, CancellationToken.None);

                    if (existing == null)
                    {
                        // Create new document style
                        await seedStyle.Save(CancellationToken.None);
                        seededCount++;
                        Console.WriteLine($"[Meridian] Seeded DocumentStyle: {seedStyle.Name} ({seedStyle.Code}) v{seedStyle.Version}");
                    }
                    else
                    {
                        // Check if update needed (version changed or content differs)
                        var needsUpdate = existing.Version != seedStyle.Version ||
                                        !AreEqual(existing, seedStyle);

                        if (needsUpdate)
                        {
                            // Preserve existing data, update from seed
                            existing.Name = seedStyle.Name;
                            existing.Code = seedStyle.Code;
                            existing.Description = seedStyle.Description;
                            existing.Version = seedStyle.Version;
                            existing.Tags = seedStyle.Tags;
                            existing.DetectionHints = seedStyle.DetectionHints;
                            existing.SignalPhrases = seedStyle.SignalPhrases;
                            existing.ExtractionStrategy = seedStyle.ExtractionStrategy;
                            existing.UsePassageRetrieval = seedStyle.UsePassageRetrieval;
                            existing.PassageRetrievalTopK = seedStyle.PassageRetrievalTopK;
                            existing.ExpandPassageContext = seedStyle.ExpandPassageContext;
                            existing.ContextWindowSize = seedStyle.ContextWindowSize;
                            existing.UpdatedAt = DateTime.UtcNow;

                            await existing.Save(CancellationToken.None);
                            updatedCount++;
                            Console.WriteLine($"[Meridian] Updated DocumentStyle: {existing.Name} to v{existing.Version}");
                        }
                    }
                }

                if (seededCount > 0 || updatedCount > 0)
                {
                    Console.WriteLine($"[Meridian] DocumentStyle seeding complete: {seededCount} created, {updatedCount} updated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Meridian] DocumentStyle seed error: {ex.Message}");
            }
        });
    }

    private static bool AreEqual(DocumentStyle existing, DocumentStyle seed)
    {
        return existing.Name == seed.Name &&
               existing.Code == seed.Code &&
               existing.Description == seed.Description &&
               existing.ExtractionStrategy == seed.ExtractionStrategy &&
               existing.UsePassageRetrieval == seed.UsePassageRetrieval &&
               existing.PassageRetrievalTopK == seed.PassageRetrievalTopK &&
               existing.ExpandPassageContext == seed.ExpandPassageContext &&
               existing.ContextWindowSize == seed.ContextWindowSize &&
               SequenceEqual(existing.Tags, seed.Tags) &&
               SequenceEqual(existing.DetectionHints, seed.DetectionHints) &&
               SequenceEqual(existing.SignalPhrases, seed.SignalPhrases);
    }

    private static bool SequenceEqual(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count) return false;
        return left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }
}
