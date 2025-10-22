using Koan.Data.Core;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Services;

public interface IMealPlanningService
{
    Task<IReadOnlyList<object>> SuggestRecipesAsync(SuggestRecipesRequest request, CancellationToken ct = default);
    Task<MealPlan> CreateMealPlanAsync(CreateMealPlanRequest request, CancellationToken ct = default);
    Task<(ShoppingList List, IReadOnlyList<ShoppingItem> Items)> GenerateShoppingListAsync(string planId, CancellationToken ct = default);
}

public sealed class MealPlanningService : IMealPlanningService
{
    public async Task<IReadOnlyList<object>> SuggestRecipesAsync(SuggestRecipesRequest request, CancellationToken ct = default)
    {
        var pantryItems = await PantryItem.All();
        var availableItems = pantryItems.Where(i => i.Status == "available").ToList();
        var recipes = (await Recipe.All()).ToList();

        if (request.DietaryRestrictions?.Length > 0)
        {
            recipes = recipes.Where(r => request.DietaryRestrictions.All(restriction =>
                r.DietaryTags.Contains(restriction, StringComparer.OrdinalIgnoreCase))).ToList();
        }

        if (request.MaxCookingMinutes.HasValue)
        {
            recipes = recipes.Where(r => r.TotalTimeMinutes <= request.MaxCookingMinutes.Value).ToList();
        }

        var scored = recipes.Select(recipe =>
        {
            var required = recipe.Ingredients.Length;
            var available = recipe.Ingredients.Count(ing =>
                availableItems.Any(item =>
                    item.Name.Contains(ing.Name, StringComparison.OrdinalIgnoreCase) ||
                    ing.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)));

            var availabilityScore = required > 0 ? (float)available / required : 0f;
            var ratingScore = recipe.AverageRating / 5f;
            var popularityScore = Math.Min(recipe.TimesCooked / 100f, 1f);
            var totalScore = (availabilityScore * 0.6f) + (ratingScore * 0.3f) + (popularityScore * 0.1f);

            return new
            {
                recipe,
                score = totalScore,
                availabilityScore,
                missingIngredients = recipe.Ingredients
                    .Where(ing => !availableItems.Any(item =>
                        item.Name.Contains(ing.Name, StringComparison.OrdinalIgnoreCase) ||
                        ing.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(i => i.Name)
                    .ToArray()
            };
        })
        .OrderByDescending(r => r.score)
        .Take(request.Limit ?? 20)
        .Cast<object>()
        .ToList();

        return scored;
    }

    public async Task<MealPlan> CreateMealPlanAsync(CreateMealPlanRequest request, CancellationToken ct = default)
    {
        var plan = new MealPlan
        {
            UserId = request.UserId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PlannedMeals = request.Meals
        };
        await plan.Save();
        return plan;
    }

    public async Task<(ShoppingList List, IReadOnlyList<ShoppingItem> Items)> GenerateShoppingListAsync(string planId, CancellationToken ct = default)
    {
        var plan = await MealPlan.Get(planId);
        if (plan == null) throw new InvalidOperationException("Meal plan not found");

        var pantryItems = (await PantryItem.All()).Where(i => i.Status == "available").ToList();
        var neededItems = new List<ShoppingItem>();

        foreach (var meal in plan.PlannedMeals)
        {
            var recipe = await Recipe.Get(meal.RecipeId);
            if (recipe == null) continue;

            foreach (var ingredient in recipe.Ingredients)
            {
                var available = pantryItems.FirstOrDefault(p =>
                    p.Name.Contains(ingredient.Name, StringComparison.OrdinalIgnoreCase) ||
                    ingredient.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase));

                if (available == null || available.Quantity < ingredient.Amount)
                {
                    var needed = neededItems.FirstOrDefault(n => n.Name == ingredient.Name);
                    if (needed != null)
                    {
                        needed.Quantity += ingredient.Amount - (available?.Quantity ?? 0);
                    }
                    else
                    {
                        neededItems.Add(new ShoppingItem
                        {
                            Name = ingredient.Name,
                            Quantity = ingredient.Amount - (available?.Quantity ?? 0),
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

        return (shoppingList, neededItems);
    }
}
