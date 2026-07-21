using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

[Route("api/an-makers")]
public sealed class MakersController : EntityController<Maker>
{
}

[Route("api/an-works")]
public sealed class WorksController : EntityController<Work>
{
}
