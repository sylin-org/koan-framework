using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Web.Sort.Tests;

[Route("api/widgets")]
public sealed class WidgetController : EntityController<Widget>
{
}
