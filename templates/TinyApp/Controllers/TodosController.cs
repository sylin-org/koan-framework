using Microsoft.AspNetCore.Mvc;
using Sora.Web.Controllers;
using TinyApp.Models;

namespace TinyApp.Controllers;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }
