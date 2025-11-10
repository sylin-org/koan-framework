using Koan.Context.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API for managing search categories
/// </summary>
/// <remarks>
/// Auto-generated endpoints via EntityController:
/// - GET    /api/searchcategories           - List all categories
/// - POST   /api/searchcategories           - Create new category
/// - GET    /api/searchcategories/{id}      - Get category by ID
/// - PATCH  /api/searchcategories/{id}      - Update category
/// - DELETE /api/searchcategories/{id}      - Delete category
/// - GET    /api/searchcategories/query     - Query with filters
/// </remarks>
[Route("api/searchcategories")]
[ApiController]
public class SearchCategoryController : EntityController<SearchCategory>
{
    // EntityController<T> provides all CRUD operations
    // No additional code needed for MVP
}
