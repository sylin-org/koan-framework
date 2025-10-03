using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Koan.Web.Connector.GraphQl.Execution;
using System.ComponentModel.DataAnnotations;

namespace Koan.Web.Connector.GraphQl.Controllers;

[ApiController]
[Route("graphql")]
public sealed class GraphQlController : ControllerBase
{
    private readonly IGraphQlExecutor _executor;
    private readonly ILogger<GraphQlController> _log;
    public GraphQlController(IGraphQlExecutor executor, ILogger<GraphQlController> log)
    {
        _executor = executor; _log = log;
    }

    public sealed class GraphQlRequest
    {
        [Required]
        public string? Query { get; set; }
        public object? Variables { get; set; }
        public string? OperationName { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] GraphQlRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required" });

        var payload = await _executor.Execute(request.Query, request.Variables, request.OperationName, HttpContext, ct);
        return Ok(payload);
    }
}


