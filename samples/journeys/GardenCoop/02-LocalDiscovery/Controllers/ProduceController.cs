using GardenCoop.Infrastructure;
using GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoop.Controllers;

/// <summary>The cooperative harvest is available through the conventional Entity HTTP surface.</summary>
[Route(GardenApiRoutes.Produce)]
public sealed class ProduceController : EntityController<Produce>;
