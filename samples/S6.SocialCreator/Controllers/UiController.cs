using Microsoft.AspNetCore.Mvc;

namespace S6.SocialCreator.Controllers;

[Route("")]
public sealed class UiController : Controller
{
    // Serve the unified UI at the root path
    [HttpGet]
    [Route("")]
    public IActionResult Index() => PhysicalFile(Path.Combine(Environment.CurrentDirectory, "wwwroot", "index.html"), "text/html");
}
