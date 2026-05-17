using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace Koan.Web.Sort.Tests;

/// <summary>
/// Exercises <see cref="PaginationAttribute.DefaultSort"/> — when no <c>?sort=</c> is provided,
/// the attribute's default sort should be applied through the new strict parser.
/// </summary>
[Route("api/widgets-default-sort")]
[Pagination(DefaultSort = "-CreatedAt,Name")]
public sealed class WidgetDefaultSortController : EntityController<Widget>
{
}
