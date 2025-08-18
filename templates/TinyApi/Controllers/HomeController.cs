using Microsoft.AspNetCore.Mvc;

namespace TinyApi.Controllers;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Redirect("/swagger");
}
