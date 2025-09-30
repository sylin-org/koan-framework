using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

// EntityController<Member> gives us full CRUD for free
// admin UI uses this to manage co-op members
[ApiController]
[Route("api/garden/members")]
public sealed class MembersController : EntityController<Member>
{
    // no code needed - framework handles everything
}
