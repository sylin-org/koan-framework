using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Models;
using S16.PantryPal.Services;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Controllers;

/// <summary>
/// Meal planning and recipe suggestions controller.
/// Uses semantic search for recipe matching based on available pantry items.
/// </summary>
[ApiController]
[Route("api/meals")]
public class MealsController(IMealPlanningService service) : ControllerBase
{
    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestRecipes([FromBody] SuggestRecipesRequest request, CancellationToken ct = default)
    {
        var scored = await service.SuggestRecipesAsync(request, ct);
        return Ok(scored);
    }

    [HttpPost("plan")]
    public async Task<IActionResult> CreateMealPlan([FromBody] CreateMealPlanRequest request, CancellationToken ct = default)
    {
        var plan = await service.CreateMealPlanAsync(request, ct);
        return Ok(new { planId = plan.Id, plan });
    }

    [HttpPost("shopping/{planId}")]
    public async Task<IActionResult> GenerateShoppingList(string planId, CancellationToken ct = default)
    {
        try
        {
            var result = await service.GenerateShoppingListAsync(planId, ct);
            return Ok(new { listId = result.List.Id, items = result.Items });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
