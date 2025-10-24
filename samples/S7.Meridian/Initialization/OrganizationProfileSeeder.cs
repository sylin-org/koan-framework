using Koan.Core;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.SeedData;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.Meridian.Initialization;

/// <summary>
/// Seeds default OrganizationProfile templates for Meridian.
/// Follows Koan pattern: static seed data in SeedData folder, initialization via IKoanInitializer.
/// </summary>
public class OrganizationProfileSeeder : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Use background task to seed after app starts (allows Entity framework to be ready)
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000); // Wait for app to fully start

            try
            {
                var profiles = OrganizationProfileSeedData.GetProfiles();
                var seededCount = 0;
                var updatedCount = 0;
                var activatedCount = 0;

                foreach (var seedProfile in profiles)
                {
                    var existing = await OrganizationProfile.Get(seedProfile.Id, CancellationToken.None);

                    if (existing == null)
                    {
                        // Create new profile
                        // Ensure only one profile is active: if this is marked active, deactivate others first
                        if (seedProfile.Active)
                        {
                            await DeactivateAllProfilesAsync();
                        }

                        await seedProfile.Save(CancellationToken.None);
                        seededCount++;
                        Console.WriteLine($"[Meridian] Seeded OrganizationProfile: {seedProfile.Name} (Active: {seedProfile.Active})");

                        if (seedProfile.Active)
                        {
                            activatedCount++;
                        }
                    }
                    else
                    {
                        // Check if update needed (field definitions or name changed)
                        var needsUpdate = !AreEqual(existing, seedProfile);

                        if (needsUpdate)
                        {
                            // Preserve Active state unless this is a seed update that explicitly sets Active=true
                            var shouldActivate = seedProfile.Active && !existing.Active;

                            // Update from seed
                            existing.Name = seedProfile.Name;
                            existing.Fields = seedProfile.Fields;
                            existing.UpdatedAt = DateTime.UtcNow;

                            if (shouldActivate)
                            {
                                await existing.ActivateAsync(CancellationToken.None);
                                activatedCount++;
                                Console.WriteLine($"[Meridian] Updated and activated OrganizationProfile: {existing.Name}");
                            }
                            else
                            {
                                await existing.Save(CancellationToken.None);
                                Console.WriteLine($"[Meridian] Updated OrganizationProfile: {existing.Name}");
                            }

                            updatedCount++;
                        }
                    }
                }

                // Ensure at least one profile is active
                await EnsureAtLeastOneActiveAsync();

                if (seededCount > 0 || updatedCount > 0)
                {
                    Console.WriteLine($"[Meridian] OrganizationProfile seeding complete: {seededCount} created, {updatedCount} updated, {activatedCount} activated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Meridian] OrganizationProfile seed error: {ex.Message}");
            }
        });
    }

    private static async Task DeactivateAllProfilesAsync()
    {
        var allProfiles = await OrganizationProfile.All(CancellationToken.None);
        foreach (var profile in allProfiles.Where(p => p.Active))
        {
            profile.Active = false;
            profile.UpdatedAt = DateTime.UtcNow;
            await profile.Save(CancellationToken.None);
        }
    }

    private static async Task EnsureAtLeastOneActiveAsync()
    {
        var activeProfile = await OrganizationProfile.GetActiveAsync(CancellationToken.None);
        if (activeProfile == null)
        {
            // No active profile, activate the first one
            var allProfiles = await OrganizationProfile.All(CancellationToken.None);
            var firstProfile = allProfiles.FirstOrDefault();
            if (firstProfile != null)
            {
                await firstProfile.ActivateAsync(CancellationToken.None);
                Console.WriteLine($"[Meridian] Auto-activated first OrganizationProfile: {firstProfile.Name}");
            }
        }
    }

    private static bool AreEqual(OrganizationProfile existing, OrganizationProfile seed)
    {
        if (existing.Name != seed.Name) return false;
        if (existing.Fields.Count != seed.Fields.Count) return false;

        // Compare field definitions
        for (int i = 0; i < existing.Fields.Count; i++)
        {
            var existingField = existing.Fields[i];
            var seedField = seed.Fields.FirstOrDefault(f => f.FieldName == existingField.FieldName);

            if (seedField == null) return false;

            if (existingField.Description != seedField.Description) return false;
            if (existingField.DisplayOrder != seedField.DisplayOrder) return false;
            if (!SequenceEqual(existingField.Examples, seedField.Examples)) return false;
        }

        return true;
    }

    private static bool SequenceEqual(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count) return false;
        return left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }
}
