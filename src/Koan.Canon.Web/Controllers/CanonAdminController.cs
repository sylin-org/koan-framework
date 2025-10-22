using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Canon.Web.Controllers;

/// <summary>
/// Administrative operations for the canon runtime.
/// </summary>
[ApiController]
[Route(WebConstants.Routes.Admin)]
public sealed class CanonAdminController : ControllerBase
{
    private static readonly MethodInfo RebuildViewsMethod = typeof(ICanonRuntime).GetMethod(nameof(ICanonRuntime.RebuildViews))!;

    private readonly ICanonRuntime _runtime;
    private readonly ICanonModelCatalog _catalog;

    public CanonAdminController(ICanonRuntime runtime, ICanonModelCatalog catalog)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    [HttpGet("records")]
    public async Task<ActionResult<IEnumerable<CanonizationRecord>>> GetRecords([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var records = new List<CanonizationRecord>();
        await foreach (var record in _runtime.Replay(from, to, ct))
        {
            records.Add(record);
        }

        return Ok(records);
    }

    [HttpPost("{slug}/rebuild")]
    public async Task<IActionResult> RebuildViews([FromRoute] string slug, [FromBody] RebuildViewsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest(new { error = "Slug must be provided." });
        }

        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.CanonicalId))
        {
            return ValidationProblem("canonicalId is required");
        }

        if (!_catalog.TryGetBySlug(slug, out var descriptor) || descriptor.IsValueObject)
        {
            return NotFound();
        }

        var method = RebuildViewsMethod.MakeGenericMethod(descriptor.ModelType);
        try
        {
            var task = (Task)method.Invoke(_runtime, new object?[] { request.CanonicalId, request.Views, ct })!;
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException ex)
        {
            return Problem(ex.InnerException?.Message ?? ex.Message);
        }

        return Accepted();
    }

    public sealed record RebuildViewsRequest(string CanonicalId, string[]? Views);
}
