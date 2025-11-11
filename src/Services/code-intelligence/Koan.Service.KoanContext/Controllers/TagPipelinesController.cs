using Koan.Context.Filters;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// Manages tag pipelines that orchestrate rule execution.
/// </summary>
[ApiController]
[Route(Constants.Routes.TagPipelines)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagPipelinesController : EntityController<TagPipeline>
{
    protected override string GetDisplay(TagPipeline entity)
    {
        if (entity is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(entity.Name) ? base.GetDisplay(entity) : entity.Name;
    }
}