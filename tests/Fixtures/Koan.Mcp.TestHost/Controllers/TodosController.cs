using Koan.Mcp.TestHost.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Mcp.TestHost.Controllers;

[Route("api/[controller]")]
public sealed class TodosController : EntityController<Todo>
{
    // Inherit all CRUD from EntityController
}