using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sora.Flow.Model;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("views")] // /views/{view}/{referenceId}
public sealed class ViewsController : ControllerBase
{
    private readonly ILogger<ViewsController> _logger;

    public ViewsController(ILogger<ViewsController> logger)
    {
        _logger = logger;
    }

    [HttpGet("{view}/{referenceId}")]
    public async Task<IActionResult> GetOne([FromRoute] string view, [FromRoute] string referenceId, CancellationToken ct)
    {
        // Query by ReferenceId within the per-view set using the correct projection type
        if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
        {
            var list = await CanonicalProjectionView.Query($"ReferenceId == '{referenceId}'", view, ct);
            var doc = list.FirstOrDefault();
            _logger.LogInformation("ViewsController.GetOne canonical view={View} ref={Ref} found={Found}", view, referenceId, doc is not null);
            return doc is null ? NotFound() : Ok(doc);
        }
        if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
        {
            var list = await LineageProjectionView.Query($"ReferenceId == '{referenceId}'", view, ct);
            var doc = list.FirstOrDefault();
            _logger.LogInformation("ViewsController.GetOne lineage view={View} ref={Ref} found={Found}", view, referenceId, doc is not null);
            return doc is null ? NotFound() : Ok(doc);
        }

        // Fallback generic
        var generic = await ProjectionView<object>.Query($"ReferenceId == '{referenceId}'", view, ct);
        var gdoc = generic.FirstOrDefault();
        _logger.LogInformation("ViewsController.GetOne generic view={View} ref={Ref} found={Found}", view, referenceId, gdoc is not null);
        return gdoc is null ? NotFound() : Ok(gdoc);
    }

    [HttpGet("{view}")]
    public async Task<IActionResult> GetPage([FromRoute] string view, [FromQuery] string? q, [FromQuery] int? page = 1, [FromQuery] int? size = 50, CancellationToken ct = default)
    {
        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(size ?? 50, 1, 500);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Filter within the per-view set using the correct projection type
            if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
            {
                var results = await CanonicalProjectionView.Query(q!, view, ct);
                var total = results.Count;
                var skip = (p - 1) * s;
                var pageItems = results.Skip(skip).Take(s).ToList();
                var hasNext = skip + pageItems.Count < total;
                _logger.LogInformation("ViewsController.GetPage canonical view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, total, p, s, pageItems.Count);
                return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
            }
            if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
            {
                var results = await LineageProjectionView.Query(q!, view, ct);
                var total = results.Count;
                var skip = (p - 1) * s;
                var pageItems = results.Skip(skip).Take(s).ToList();
                var hasNext = skip + pageItems.Count < total;
                _logger.LogInformation("ViewsController.GetPage lineage view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, total, p, s, pageItems.Count);
                return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
            }

            // Fallback generic
            var generic = await ProjectionView<object>.Query(q!, view, ct);
            var gtotal = generic.Count;
            var gskip = (p - 1) * s;
            var gitems = generic.Skip(gskip).Take(s).ToList();
            var gnext = gskip + gitems.Count < gtotal;
            _logger.LogInformation("ViewsController.GetPage generic view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, gtotal, p, s, gitems.Count);
            return Ok(new { page = p, size = s, total = gtotal, hasNext = gnext, items = gitems });
        }

        // Unfiltered: page within the per-view set using the correct projection type
        using (Sora.Data.Core.DataSetContext.With(view))
        {
            if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
            {
                var items = await CanonicalProjectionView.Page(p, s, ct);
                var hasNext = items.Count == s; // heuristic
                _logger.LogInformation("ViewsController.GetPage canonical view={View} page={Page} size={Size} returned={Returned}", view, p, s, items.Count);
                return Ok(new { page = p, size = s, hasNext, items });
            }
            if (string.Equals(view, Sora.Flow.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
            {
                var items = await LineageProjectionView.Page(p, s, ct);
                var hasNext = items.Count == s; // heuristic
                _logger.LogInformation("ViewsController.GetPage lineage view={View} page={Page} size={Size} returned={Returned}", view, p, s, items.Count);
                return Ok(new { page = p, size = s, hasNext, items });
            }

            var generic = await ProjectionView<object>.Page(p, s, ct);
            var gHasNext = generic.Count == s; // heuristic
            _logger.LogInformation("ViewsController.GetPage generic view={View} page={Page} size={Size} returned={Returned}", view, p, s, generic.Count);
            return Ok(new { page = p, size = s, hasNext = gHasNext, items = generic });
        }
    }
}
