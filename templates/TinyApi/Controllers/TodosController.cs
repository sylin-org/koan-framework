using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using TinyApi.Models;

namespace TinyApi.Controllers;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo> { }
