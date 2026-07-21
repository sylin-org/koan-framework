using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;

namespace SnapVault.Controllers;

/// <summary>
/// Studio events exposed through EntityController. Pagination is off because the sidebar needs the complete
/// tenant-scoped list, including daily albums created over time.
/// </summary>
[Route("api/events")]
[Pagination(Mode = PaginationMode.Off)]
[OperatorOnly]
public sealed class EventsController : EntityController<Event>
{
}
