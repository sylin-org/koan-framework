using Microsoft.AspNetCore.Mvc;
using Sora.Web.Controllers;
using TinyApi.Models;

namespace TinyApi.Controllers;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }
