using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace TaskGraph;

[Route("api/todo-items")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 200)]
public sealed class TodoItemController : EntityController<TodoItem>;
