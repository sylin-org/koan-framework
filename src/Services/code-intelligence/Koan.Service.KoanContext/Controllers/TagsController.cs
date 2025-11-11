using Koan.Context.Filters;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// Manages tag vocabulary entries that drive rule resolution and search experiences.
/// </summary>
[ApiController]
[Route(Constants.Routes.Tags)]
[ServiceFilter(typeof(PartitionScopeFilter))]
public sealed class TagsController : EntityController<TagVocabularyEntry>
{
	protected override string GetDisplay(TagVocabularyEntry entity)
	{
		if (entity is null)
		{
			return string.Empty;
		}

		return string.IsNullOrWhiteSpace(entity.Tag) ? base.GetDisplay(entity) : entity.Tag;
	}
}
