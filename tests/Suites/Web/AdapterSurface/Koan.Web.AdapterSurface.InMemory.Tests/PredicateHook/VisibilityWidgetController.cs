using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;

[Route("api/visibility-widgets")]
public sealed class VisibilityWidgetController : EntityController<VisibilityWidget>
{
}
