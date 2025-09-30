using Microsoft.AspNetCore.Mvc;
using Koan.Web.Connector.GraphQl.Execution;

namespace Koan.Web.Connector.GraphQl.Controllers;

[ApiController]
[Route("graphql/sdl")]
public sealed class GraphQlSdlController : ControllerBase
{
    private readonly IGraphQlExecutor _executor;
    public GraphQlSdlController(IGraphQlExecutor executor) { _executor = executor; }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var sdl = await _executor.GetSdl(ct);
        return Content(sdl, "text/plain");
    }
}

