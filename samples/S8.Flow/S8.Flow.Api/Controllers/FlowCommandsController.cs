using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared.Commands;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/flow/commands")]
public sealed class FlowCommandsController : ControllerBase
{
    [HttpPost("{name}")]
    public async Task<IActionResult> Post(string name, CancellationToken ct)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Request.Query)
            dict[kv.Key] = Coerce(kv.Value);
        string? target = dict.Remove("target", out var tVal) ? tVal?.ToString() : null;
        // Optional: merge JSON body
        if (Request.ContentLength > 0 && Request.ContentType?.Contains("application/json") == true)
        {
            using var sr = new StreamReader(Request.Body);
            var json = await sr.ReadToEndAsync(ct);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (obj != null)
                    foreach (var p in obj)
                        if (!dict.ContainsKey(p.Key))
                            dict[p.Key] = CoerceJson(p.Value);
            }
        }
        FlowCommand.Send(name, dict, target: target, source: "api");
        return Accepted(new { status = "accepted", command = name, target, args = dict });
    }

    private static object? Coerce(string? v)
    {
        if (v == null) return null;
        if (int.TryParse(v, out var i)) return i;
        if (long.TryParse(v, out var l)) return l;
        if (double.TryParse(v, out var d)) return d;
        if (bool.TryParse(v, out var b)) return b;
        if (TimeSpan.TryParse(v, out var ts)) return ts;
        return v;
    }
    private static object? CoerceJson(JsonElement e)
    {
        return e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetInt64(out var l) => l,
            JsonValueKind.Number when e.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Null => null,
            _ => e.ToString()
        };
    }
}
