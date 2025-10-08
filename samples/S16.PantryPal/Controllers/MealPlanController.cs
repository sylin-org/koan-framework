using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Models;

namespace S16.PantryPal.Controllers;

/// <summary>
/// Meal planning and recipe suggestions controller.
/// Uses semantic search for recipe matching based on available pantry items.
/// </summary>
[ApiController]
[Route("api/meals")]
public class MealsController : ControllerBase
{
    /// <summary>
    /// Suggest recipes based on available pantry items and preferences.
    /// </summary>
    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestRecipes(
        [FromBody] SuggestRecipesRequest request,
        CancellationToken ct = default)
    {
        var pantryItems = await PantryItem.All();
        var availableItems = pantryItems
            .Where(i => i.Status == "available")
            .ToList();

        var recipes = await Recipe.All();

        // Filter by dietary restrictions
        if (request.DietaryRestrictions?.Length > 0)
        {
            recipes = recipes.Where(r =>
                request.DietaryRestrictions.All(restriction =>
                    r.DietaryTags.Contains(restriction, StringComparer.OrdinalIgnoreCase)));
        }

        // Filter by cooking time
        if (request.MaxCookingMinutes.HasValue)
        {
            recipes = recipes.Where(r => r.TotalTimeMinutes <= request.MaxCookingMinutes.Value);
        }

        // Score recipes by ingredient availability
        var scoredRecipes = recipes.Select(recipe =>
        {
            var requiredIngredients = recipe.Ingredients.Length;
            var availableIngredients = recipe.Ingredients.Count(ingredient =>
                availableItems.Any(item =>
                    item.Name.Contains(ingredient.Name, StringComparison.OrdinalIgnoreCase) ||
                    ingredient.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)));

            var availabilityScore = requiredIngredients > 0
                ? (float)availableIngredients / requiredIngredients
                : 0f;

            var ratingScore = recipe.AverageRating / 5f;
            var popularityScore = Math.Min(recipe.TimesCooked / 100f, 1f);

            var totalScore = (availabilityScore * 0.6f) + (ratingScore * 0.3f) + (popularityScore * 0.1f);

            return new
            {
                recipe,
                score = totalScore,
                availabilityScore,
                missingIngredients = recipe.Ingredients
                    .Where(ingredient => !availableItems.Any(item =>
                        item.Name.Contains(ingredient.Name, StringComparison.OrdinalIgnoreCase) ||
                        ingredient.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(i => i.Name)
                    .ToArray()
            };
        })
        .OrderByDescending(r => r.score)
        .Take(request.Limit ?? 20)
        .ToList();

        return Ok(scoredRecipes);
    }

    /// <summary>
    /// Create meal plan for specified date range.
    /// </summary>
    [HttpPost("plan")]
    public async Task<IActionResult> CreateMealPlan(
        [FromBody] CreateMealPlanRequest request,
        CancellationToken ct = default)
    {
        var plan = new MealPlan
        {
            UserId = request.UserId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PlannedMeals = request.Meals
        };

        await plan.Save();

        return Ok(new { planId = plan.Id, plan });
    }

    /// <summary>
    /// Generate shopping list from meal plan.
    /// </summary>
    [HttpPost("shopping/{planId}")]
    public async Task<IActionResult> GenerateShoppingList(
        string planId,
        CancellationToken ct = default)
    {
        var plan = await MealPlan.Get(planId);
        if (plan == null)
            return NotFound(new { error = "Meal plan not found" });

        var pantryItems = (await PantryItem.All()).Where(i => i.Status == "available").ToList();
        var neededItems = new List<ShoppingItem>();

        foreach (var meal in plan.PlannedMeals)
        {
            var recipe = await Recipe.Get(meal.RecipeId);
            if (recipe == null)
                continue;

            foreach (var ingredient in recipe.Ingredients)
            {
                // Check if we have this ingredient in pantry
                var available = pantryItems.FirstOrDefault(p =>
                    p.Name.Contains(ingredient.Name, StringComparison.OrdinalIgnoreCase) ||
                    ingredient.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase));

                if (available == null || available.Quantity < ingredient.Quantity)
                {
                    var needed = neededItems.FirstOrDefault(n => n.Name == ingredient.Name);
                    if (needed != null)
                    {
                        needed.Quantity += ingredient.Quantity - (available?.Quantity ?? 0);
                    }
                    else
                    {
                        neededItems.Add(new ShoppingItem
                        {
                            Name = ingredient.Name,
                            Quantity = ingredient.Quantity - (available?.Quantity ?? 0),
                            Unit = ingredient.Unit,
                            Category = available?.Category ?? "uncategorized",
                            IsPurchased = false
                        });
                    }
                }
            }
        }

        var shoppingList = new ShoppingList
        {
            Name = $"Shopping for {plan.StartDate:MMM dd} - {plan.EndDate:MMM dd}",
            MealPlanId = planId,
            Items = neededItems.ToArray(),
            Status = "active"
        };

        await shoppingList.Save();

        return Ok(new { listId = shoppingList.Id, items = neededItems });
    }
}

public class SuggestRecipesRequest
{
    public string? UserId { get; set; }
    public string[]? DietaryRestrictions { get; set; }
    public int? MaxCookingMinutes { get; set; }
    public int? Limit { get; set; } = 20;
}

public class CreateMealPlanRequest
{
    public string? UserId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public PlannedMeal[] Meals { get; set; } = Array.Empty<PlannedMeal>();
}
