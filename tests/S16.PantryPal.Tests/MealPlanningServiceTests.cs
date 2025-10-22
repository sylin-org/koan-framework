using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using Koan.Data.Core.Model;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Tests;

[Collection("KoanHost")]
public class MealPlanningServiceTests
{
    [Fact]
    public async Task SuggestRecipes_ShouldScoreAndLimit()
    {
    var p1 = new PantryItem { Name = "pasta", Status = "available" };
    await p1.Save();
    var p2 = new PantryItem { Name = "tomato", Status = "available" };
    await p2.Save();

        var r = new Recipe
        {
            Name = "Pasta Pomodoro",
            Ingredients = new [] { new RecipeIngredient { Name = "pasta", Amount = 1, Unit = "whole" }, new RecipeIngredient { Name = "tomato", Amount = 1, Unit = "whole" } },
            DietaryTags = new [] { "vegetarian" },
            TotalTimeMinutes = 30,
            AverageRating = 4.5f,
            TimesCooked = 50
        };
        await r.Save();

        var svc = new MealPlanningService();
        var result = await svc.SuggestRecipesAsync(new SuggestRecipesRequest { DietaryRestrictions = new []{"vegetarian"}, MaxCookingMinutes = 45, Limit = 5 });
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateMealPlan_ShouldPersist()
    {
        var svc = new MealPlanningService();
        var plan = await svc.CreateMealPlanAsync(new CreateMealPlanRequest{ StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(2), Meals = Array.Empty<PlannedMeal>() });
        (await MealPlan.Get(plan.Id)).Should().NotBeNull();
    }
}
