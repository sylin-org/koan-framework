using Microsoft.AspNetCore.Mvc;

namespace TinyDockerApp.Controllers;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Redirect("/index.html");
}
