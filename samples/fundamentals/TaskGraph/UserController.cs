using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace TaskGraph;

[Route("api/users")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 200)]
public sealed class UserController : EntityController<User>;
