using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace TaskGraph;

[Route("api/categories")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 200)]
public sealed class CategoryController : EntityController<Category>;
