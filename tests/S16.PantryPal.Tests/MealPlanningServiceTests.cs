using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Tests;

public class MealPlanningServiceTests
{
    [Fact]
    public async Task SuggestRecipes_ShouldScoreAndLimit()
    {
        await new PantryItem { Name = "pasta", Status = "available" }.Save();
        await new PantryItem { Name = "tomato", Status = "available" }.Save();

        await new Recipe
        {
            Name = "Pasta Pomodoro",
            Ingredients = new [] { new RecipeIngredient { Name = "pasta", Amount = 1, Unit = "whole" }, new RecipeIngredient { Name = "tomato", Amount = 1, Unit = "whole" } },
            DietaryTags = new [] { "vegetarian" },
            TotalTimeMinutes = 30,
            AverageRating = 4.5f,
            TimesCooked = 50
        }.Save();

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
