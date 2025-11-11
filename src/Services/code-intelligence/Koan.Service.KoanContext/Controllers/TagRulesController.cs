using Koan.Context.Filters;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// Manages declarative tag rules used by tag pipelines to infer metadata.
/// </summary>
[ApiController]
[Route(Constants.Routes.TagRules)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagRulesController : EntityController<TagRule>
{
    protected override string GetDisplay(TagRule entity)
    {
        if (entity is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(entity.Name) ? base.GetDisplay(entity) : entity.Name;
    }
}