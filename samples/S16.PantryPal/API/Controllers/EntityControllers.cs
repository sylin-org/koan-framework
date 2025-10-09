using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Models;
using Koan.Web.Controllers;
using Koan.Web.Attributes;

namespace S16.PantryPal.Controllers;

[ApiController]
[Route("api/data/recipes")]
public class RecipeController : EntityController<Recipe, string> { }

[ApiController]
[Route("api/data/pantry")]
 [Pagination(Mode = PaginationMode.On, DefaultSize = 25, MaxSize = 200, IncludeCount = true, DefaultSort = "-ExpiresAt,Name")]
public class PantryItemController : EntityController<PantryItem, string> { }

[ApiController]
[Route("api/data/photos")]
public class PantryPhotoController : EntityController<PantryPhoto, string> { }

[ApiController]
[Route("api/data/mealplans")]
public class MealPlanController : EntityController<MealPlan, string> { }

[ApiController]
[Route("api/data/shopping")]
public class ShoppingListController : EntityController<ShoppingList, string> { }

[ApiController]
[Route("api/data/profiles")]
public class UserProfileController : EntityController<UserProfile, string> { }

[ApiController]
[Route("api/data/visionsettings")]
public class VisionSettingsController : EntityController<VisionSettings, string> { }
