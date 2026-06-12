using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Web.AdapterSurface.TestKit;

[Route("api/widgets")]
public sealed class WidgetController : EntityController<Widget>
{
}
