using Koan.Context.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// REST API for managing search audiences
/// </summary>
/// <remarks>
/// Auto-generated endpoints via EntityController:
/// - GET    /api/searchaudiences           - List all audiences
/// - POST   /api/searchaudiences           - Create new audience
/// - GET    /api/searchaudiences/{id}      - Get audience by ID
/// - PATCH  /api/searchaudiences/{id}      - Update audience
/// - DELETE /api/searchaudiences/{id}      - Delete audience
/// - GET    /api/searchaudiences/query     - Query with filters
/// </remarks>
[Route("api/searchaudiences")]
[ApiController]
public class SearchAudienceController : EntityController<SearchAudience>
{
    // EntityController<T> provides all CRUD operations
    // No additional code needed for MVP
}
