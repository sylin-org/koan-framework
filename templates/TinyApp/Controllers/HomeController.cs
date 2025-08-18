using Microsoft.AspNetCore.Mvc;

namespace TinyApp.Controllers;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Redirect("/swagger");
}
