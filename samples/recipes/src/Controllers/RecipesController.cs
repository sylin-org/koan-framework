using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

[Route("api/recipes")]
public sealed class RecipesController : EntityController<Recipe>;
