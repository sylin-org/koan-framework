using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using RecipeApi.Domain;
using RecipeApi.Infrastructure;

namespace RecipeApi.Web;

[Route(ApplicationConstants.Routes.Recipes)]
public sealed class RecipesController : EntityController<Recipe>
{
    [HttpPut("{id}")]
    public Task<IActionResult> Put(
        [FromRoute] string id,
        [FromBody] Recipe recipe,
        CancellationToken ct)
    {
        recipe.Id = id;
        return Upsert(recipe, ct);
    }
}
