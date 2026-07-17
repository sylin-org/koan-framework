using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;

namespace SnapVault.Controllers;

/// <summary>
/// Events surface (#21 list, #22 create). An <see cref="EntityController{Event}"/> so both come free
/// (Reference = Intent) — no hand-written CRUD. Pagination is <b>Off</b> on purpose: the sidebar needs the
/// studio's <i>whole</i> event list as a bare JSON array, and DailyAuto albums accrue one-per-day, so the
/// framework's default 50-row page would silently truncate a long-running studio. <c>Type</c> serializes as its
/// enum ordinal, and the SPA's upload dropdown filters out DailyAuto (ordinal 5) client-side. Isolation rides the
/// ambient tenant axis (no per-endpoint auth — the established
/// SnapVault pattern: a studio operator sees only their own tenant's events).
/// </summary>
[Route("api/events")]
[Pagination(Mode = PaginationMode.Off)]
[OperatorOnly]   // Events are only tenant-scoped (not [AccessScoped]); a guest must not list/create the studio's events
public sealed class EventsController : EntityController<Event>
{
}
