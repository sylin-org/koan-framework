using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

// EntityController<Sensor> gives us full CRUD for free
// GET, POST, PATCH, DELETE all work automatically - no code needed!
[ApiController]
[Route("api/garden/sensors")]
public sealed class SensorsController : EntityController<Sensor>
{
    // that's it - empty class but fully functional API
    // admin UI uses this to list sensors and bind them to plots
}
