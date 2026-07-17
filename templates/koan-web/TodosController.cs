using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace KoanWebApp;

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
