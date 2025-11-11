using Koan.Context.Filters;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// Manages search personas that define retrieval defaults and boosts.
/// </summary>
[ApiController]
[Route(Constants.Routes.SearchPersonas)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class SearchPersonasController : EntityController<SearchPersona>
{
    protected override string GetDisplay(SearchPersona entity)
    {
        if (entity is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(entity.DisplayName) ? entity.Name : entity.DisplayName;
    }
}