using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

[ApiController]
[Route("api/garden/members")]
public sealed class MembersController : EntityController<Member>
{
}
