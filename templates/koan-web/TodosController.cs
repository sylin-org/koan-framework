using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace KoanWebApp;

// Deriving from EntityController<Todo> wires the full REST surface for Todo:
//   GET /api/todos (list + ?filter=&sort=&page=)   GET /api/todos/{id}
//   POST /api/todos   PUT /api/todos/{id}   PATCH /api/todos/{id}   DELETE /api/todos/{id}
//   POST /api/todos/query (body filter/sort/paging)
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>
{
}
