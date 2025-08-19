using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using HotChocolate.Execution;
using System.Diagnostics;

namespace Sora.Web.GraphQl.Controllers;

[ApiController]
[Route("graphql")]
public sealed class GraphQlController : ControllerBase
{
    private readonly IRequestExecutorResolver _executors;
    private readonly IConfiguration _cfg;
    private readonly ILogger<GraphQlController> _log;
    public GraphQlController(IRequestExecutorResolver executors, IConfiguration cfg, ILogger<GraphQlController> log)
    {
        _executors = executors; _cfg = cfg; _log = log;
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
        var varsDict = ToDict(request.Variables);
        var debugEnabled = IsDebugEnabled();
        var varShape = DescribeVariables(varsDict);
        var varsType = request.Variables?.GetType().FullName;

        var corr = Activity.Current?.Id;
        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation("GraphQL request op={Operation} corr={CorrelationId} vars={Vars} varsType={VarsType}", request.OperationName ?? "", corr, JsonSerializer.Serialize(varShape), varsType);
        }

        var rq = QueryRequestBuilder.New()
            .SetQuery(request.Query)
            .SetVariableValues(varsDict)
            .SetOperation(request.OperationName)
            .SetServices(HttpContext.RequestServices)
            .Create();
        var result = await executor.ExecuteAsync(rq, ct);
        if (result is IQueryResult qr)
        {
            var payload = new Dictionary<string, object?>();
            if (qr.Data is not null) payload["data"] = qr.Data;
            if (qr.Errors is { Count: > 0 })
                payload["errors"] = qr.Errors.Select(e => new
                {
                    message = e.Message,
                    code = e.Code,
                    // Show the path for easier pinpointing; also see extensions.fieldPath from the filter
                    path = e.Path?.ToString(),
                    // Preserve any enriched diagnostics added by filters
                    extensions = e.Extensions is { Count: > 0 } ? e.Extensions : null
                });
            if (qr.Extensions is not null && qr.Extensions.Count > 0) payload["extensions"] = qr.Extensions;
        if (debugEnabled)
            {
                payload["debug"] = new
                {
                    correlationId = corr,
            variablesShape = varShape,
            variablesType = varsType
                };
            }
            return Ok(payload);
        }
        return StatusCode(500, new { error = "Unexpected GraphQL result" });
    }

    [HttpGet("sdl")]
    public async Task<IActionResult> GetSdl(CancellationToken ct)
    {
    var executor = await _executors.GetRequestExecutorAsync();
        var sdl = executor.Schema?.ToString() ?? "";
        return Content(sdl, "text/plain");
    }

    private static IReadOnlyDictionary<string, object?>? ToDict(object? vars)
    {
        if (vars is null) return null;
        if (vars is IReadOnlyDictionary<string, object?> d) return d;
        if (vars is IDictionary<string, object?> d2) return new Dictionary<string, object?>(d2);
        try
        {
            if (vars is JsonElement je)
            {
                if (je.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(je)!);
            }
            if (vars is JsonDocument jd)
            {
                if (jd.RootElement.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(jd.RootElement)!);
            }
            // System.Text.Json.Nodes.JsonObject
            if (vars is System.Text.Json.Nodes.JsonObject jobj)
            {
                using var jdoc = JsonDocument.Parse(jobj.ToJsonString());
                if (jdoc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return FilterNulls((Dictionary<string, object?>)FromJsonElement(jdoc.RootElement)!);
            }
            // Dictionary<string, JsonElement>
            if (vars is IDictionary<string, JsonElement> dje)
            {
                var tmp = new Dictionary<string, object?>();
                foreach (var kv in dje)
                {
                    tmp[kv.Key] = FromJsonElement(kv.Value);
                }
                return FilterNulls(tmp);
            }
            // Fallback: serialize then parse to JsonElement and convert
            var json = JsonSerializer.Serialize(vars);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return FilterNulls((Dictionary<string, object?>)FromJsonElement(doc.RootElement)!);
        }
        catch { return null; }
    }

    private bool IsDebugEnabled()
    {
        // Enable if caller sends X-Sora-Debug: true/1 or config sets Sora:GraphQl:Debug=true
        if (HttpContext.Request.Headers.TryGetValue("X-Sora-Debug", out var hv))
        {
            var s = hv.ToString();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        }
        var cfg = _cfg["Sora:GraphQl:Debug"];
        if (!string.IsNullOrWhiteSpace(cfg) && (string.Equals(cfg, "true", StringComparison.OrdinalIgnoreCase) || cfg == "1")) return true;
        return false;
    }

    private static object DescribeVariables(IReadOnlyDictionary<string, object?>? vars)
    {
        if (vars is null) return new { present = false };
        var shape = new Dictionary<string, object>();
        foreach (var kv in vars)
        {
            shape[kv.Key] = DescribeValue(kv.Value);
        }
        return new { present = true, count = vars.Count, shape };
    }

    private static object DescribeValue(object? v)
    {
        if (v is null) return new { type = "null" };
        return v switch
        {
            string => new { type = "string" },
            bool => new { type = "bool" },
            int => new { type = "int" },
            long => new { type = "long" },
            double => new { type = "double" },
            decimal => new { type = "decimal" },
            IReadOnlyDictionary<string, object?> obj => new { type = "object", count = obj.Count },
            IDictionary<string, object?> obj2 => new { type = "object", count = obj2.Count },
            IEnumerable<object?> list => new { type = "list" },
            _ => new { type = v.GetType().Name }
        };
    }

    private static IReadOnlyDictionary<string, object?> FilterNulls(Dictionary<string, object?> dict)
    {
        // Treat null variables as undefined by omitting them; avoids coercion issues for optional scalars
        if (dict.Count == 0) return dict;
        var anyNulls = false;
        foreach (var kv in dict)
        {
            if (kv.Value is null) { anyNulls = true; break; }
        }
        if (!anyNulls) return dict;
        var filtered = new Dictionary<string, object?>();
        foreach (var (k, v) in dict)
        {
            if (v is null) continue;
            // treat empty lists as undefined too
            if (v is IEnumerable<object?> seq && !(v is string))
            {
                // materialize once
                var arr = (seq as IList<object?>) ?? seq.ToList();
                if (arr.Count == 0) continue;
            }
            filtered[k] = v;
        }
        return filtered;
    }

    private static object? FromJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                // Prefer Int32 for GraphQL Int when possible; otherwise fall back to Int64/Double/Decimal
                if (el.TryGetInt32(out var i)) return i;
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetDecimal();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetBoolean();
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray()) list.Add(FromJsonElement(item));
                // If variables come as single-item arrays (transport quirks), unwrap them
                if (list.Count == 0) return null;
                if (list.Count == 1) return list[0];
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject()) dict[prop.Name] = FromJsonElement(prop.Value);
                return dict;
            default:
                return null;
        }
    }
}
