using S16.PantryPal.Models;

namespace S16.PantryPal.Contracts;

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
