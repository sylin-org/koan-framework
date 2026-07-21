using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Web.Controllers;
using Koan.Web.Endpoints;

namespace Koan.Web.Sort.Tests;

/// <summary>
/// Exercises <see cref="EntityEndpointOptions.LenientSort"/> via a per-controller override —
/// unresolvable sort fields should be silently skipped (with <c>Koan-Sort-Skipped</c> header)
/// instead of producing 400.
/// </summary>
[Route("api/widgets-lenient")]
public sealed class WidgetLenientController : EntityController<Widget>
{
    protected override Koan.Web.Hooks.QueryOptions BuildOptions()
    {
        var defaultOpts = HttpContext.RequestServices.GetRequiredService<IOptions<EntityEndpointOptions>>().Value;
        var lenientOpts = new EntityEndpointOptions
        {
            DefaultPageSize = defaultOpts.DefaultPageSize,
            MaxPageSize = defaultOpts.MaxPageSize,
            DefaultView = defaultOpts.DefaultView,
            AllowedShapes = defaultOpts.AllowedShapes,
            AllowRelationshipExpansion = defaultOpts.AllowRelationshipExpansion,
            LenientSort = true,
        };
        return Koan.Web.Queries.EntityQueryParser.Parse<Widget>(HttpContext.Request.Query, lenientOpts);
    }
}
