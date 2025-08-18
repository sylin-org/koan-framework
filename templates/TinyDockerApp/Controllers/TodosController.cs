using Microsoft.AspNetCore.Mvc;
using Sora.Web.Controllers;
using TinyDockerApp.Models;

namespace TinyDockerApp.Controllers;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }
