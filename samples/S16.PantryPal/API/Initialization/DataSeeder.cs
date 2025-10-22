using Koan.Core;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using S16.PantryPal.SeedData;
using S16.PantryPal.Models;

namespace S16.PantryPal.Initialization;

/// <summary>
/// Seeds initial data for PantryPal demo.
/// </summary>
public class DataSeeder : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Use a background task to seed data after app starts
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000); // Wait for app to fully start

            try
            {
                // Check if recipes already exist
                var existingRecipes = await Recipe.All();
                if (existingRecipes.Any())
                {
                    Console.WriteLine($"[PantryPal] {existingRecipes.Count()} recipes already seeded");
                    return;
                }

                // Seed recipes
                var recipes = RecipeSeedData.GetRecipes();
                var seededCount = 0;

                foreach (var recipe in recipes)
                {
                    await recipe.Save();
                    seededCount++;
                }

                Console.WriteLine($"[PantryPal] Successfully seeded {seededCount} recipes");

                // Create sample user profile
                var profile = new UserProfile
                {
                    Name = "Demo User",
                    HouseholdSize = 2,
                    DietaryRestrictions = Array.Empty<string>(),
                    Allergies = Array.Empty<string>(),
                    DislikedIngredients = Array.Empty<string>(),
                    FavoriteIngredients = new[] { "chicken", "pasta", "cheese" },
                    FavoriteCuisines = new[] { "Italian", "Mexican" },
                    TargetCalories = 2000,
                    MaxCookingTimeWeekday = 45,
                    MaxCookingTimeWeekend = 90,
                    ExperienceLevel = "intermediate",
                    WeeklyFoodBudget = 150,
                    PreferBatchCooking = true
                };

                await profile.Save();
                Console.WriteLine("[PantryPal] Created demo user profile");

                // Create sample pantry items
                var sampleItems = new[]
                {
                    new PantryItem
                    {
                        Name = "chicken breast",
                        Category = "meat",
                        Quantity = 2,
                        Unit = "lbs",
                        Location = "fridge",
                        Status = "available",
                        ExpiresAt = DateTime.UtcNow.AddDays(5),
                        AddedAt = DateTime.UtcNow
                    },
                    new PantryItem
                    {
                        Name = "pasta",
                        Category = "pantry",
                        Quantity = 2,
                        Unit = "lbs",
                        Location = "pantry",
                        Status = "available",
                        ExpiresAt = DateTime.UtcNow.AddMonths(6),
                        AddedAt = DateTime.UtcNow
                    },
                    new PantryItem
                    {
                        Name = "cheddar cheese",
                        Category = "dairy",
                        Quantity = 1,
                        Unit = "lbs",
                        Location = "fridge",
                        Status = "available",
                        ExpiresAt = DateTime.UtcNow.AddDays(14),
                        AddedAt = DateTime.UtcNow
                    },
                    new PantryItem
                    {
                        Name = "tomato sauce",
                        Category = "canned",
                        Quantity = 3,
                        Unit = "whole",
                        Location = "pantry",
                        Status = "available",
                        ExpiresAt = DateTime.UtcNow.AddYears(1),
                        AddedAt = DateTime.UtcNow
                    }
                };

                foreach (var item in sampleItems)
                {
                    await item.Save();
                }

                Console.WriteLine($"[PantryPal] Created {sampleItems.Length} sample pantry items");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PantryPal] Seed data error: {ex.Message}");
            }
        });
    }
}
