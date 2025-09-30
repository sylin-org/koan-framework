using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[Route("api/garden/plots")]
public sealed class PlotsController : EntityController<Plot>
{
}
