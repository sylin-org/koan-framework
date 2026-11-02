using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Controllers;

/// <summary>The page an application answers a problem with, to a request that prefers HTML (ARCH-0142). Register one in DI and <c>this.Answer</c> uses it;
/// without one a plain page says the same thing. The problem's <c>kind</c>, <c>gate</c> and <c>next</c> are in its extensions, as JSON has them.</summary>
public interface IProblemView
{
    IActionResult Render(HttpContext http, ProblemDetails problem);
}
