using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

// EntityController<Plot> gives us full CRUD for free
// admin UI uses this to create, edit, and manage beds
[Route("api/garden/plots")]
public sealed class PlotsController : EntityController<Plot>
{
    // no code needed - GET, POST, PATCH, DELETE all work automatically
}
