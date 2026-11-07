using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>The partition specs name partitions of their own, so this serves whichever a caller names (WEB-0087).</summary>
[Route("api/widgets")]
[Partitions(Any = true)]
public sealed class WidgetController : EntityController<Widget>
{
}
