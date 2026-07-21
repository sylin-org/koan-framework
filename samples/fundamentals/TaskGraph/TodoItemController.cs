using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace TaskGraph;

[Route("api/todo-items")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class TodoItemController : EntityController<TodoItem>;
