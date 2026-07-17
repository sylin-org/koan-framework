using g1c1.GardenCoop.Infrastructure;
using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[Route(GardenApiRoutes.Members)]
public sealed class MembersController : EntityController<Member>;
