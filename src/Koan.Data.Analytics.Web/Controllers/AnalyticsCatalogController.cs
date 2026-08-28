using Koan.Data.Analytics;
using Koan.Data.Abstractions.Analytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Analytics.Web.Controllers;

/// <summary>
/// The catalog door: the declared questions, read the same way code, agents, and scripts read
/// everything else in Koan — over HTTP, with the names the code declared.
/// </summary>
[ApiController]
[Route("analytics")]
public sealed class AnalyticsCatalogController : ControllerBase
{
    /// </summary>
    [HttpGet("catalog")]
    public IActionResult Catalog()
    {
        var entries = AnalyticsCatalog.All().Select(static question => new
        {
            question.Name,
            Entity = question.EntityType.Name,
            question.MeasureKind,
            MeasureMember = question.MeasureMember,
            GroupMember = question.GroupMember,
            Materialized = question.Projection is not null,
            question.RowCap,
            Links = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["recipes"] = $"/analytics/{question.EntityType.Name}/recipes",
                ["results"] = $"/analytics/{question.EntityType.Name}/{question.Name}"
            }
        });
        return Ok(new { Count = entries.Count(), Questions = entries });
    }
}
