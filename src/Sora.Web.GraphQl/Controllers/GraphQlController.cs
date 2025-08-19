using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using HotChocolate.Execution;

namespace Sora.Web.GraphQl.Controllers;

[ApiController]
[Route("graphql")]
public sealed class GraphQlController : ControllerBase
{
    private readonly IRequestExecutorResolver _executors;
    private readonly IConfiguration _cfg;
    public GraphQlController(IRequestExecutorResolver executors, IConfiguration cfg)
    {
        _executors = executors; _cfg = cfg;
    }

    public sealed class GraphQlRequest
    {
        public string? Query { get; set; }
        public object? Variables { get; set; }
        public string? OperationName { get; set; }
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] GraphQlRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query)) return BadRequest(new { error = "Query is required" });
        var executor = await _executors.GetRequestExecutorAsync();
        var rq = QueryRequestBuilder.New()
            .SetQuery(request.Query)
            .SetVariableValues(ToDict(request.Variables))
            .SetOperation(request.OperationName)
            .SetServices(HttpContext.RequestServices)
            .Create();
        var result = await executor.ExecuteAsync(rq, ct);
        if (result is IQueryResult qr)
        {
            var payload = new Dictionary<string, object?>();
            if (qr.Data is not null) payload["data"] = qr.Data;
            if (qr.Errors is { Count: > 0 }) payload["errors"] = qr.Errors.Select(e => new { message = e.Message, code = e.Code });
            if (qr.Extensions is not null && qr.Extensions.Count > 0) payload["extensions"] = qr.Extensions;
            return Ok(payload);
        }
        return StatusCode(500, new { error = "Unexpected GraphQL result" });
    }

    private static IReadOnlyDictionary<string, object?>? ToDict(object? vars)
    {
        if (vars is null) return null;
        if (vars is IReadOnlyDictionary<string, object?> d) return d;
        try
        {
            if (vars is JsonElement je)
            {
                if (je.ValueKind != JsonValueKind.Object) return null;
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
            }
            var json = JsonSerializer.Serialize(vars);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch { return null; }
    }
}
