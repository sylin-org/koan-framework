using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Mcp.RelationshipVisibility.Tests;

// Plain REST surface for out-of-band seeding (REST is anonymous here; the hooks still apply, so the
// fixture seeds through Data<T> directly rather than REST to avoid the write paths being affected).
[Route("api/makers")]
public sealed class MakersController : EntityController<Maker>
{
}

[Route("api/works")]
public sealed class WorksController : EntityController<Work>
{
}
