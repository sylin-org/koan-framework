using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace Koan.Web.Connector.GraphQl.Infrastructure;

public static class ResultComposer
{
    public static object Compose(IExecutionResult result, HttpContext http, IReadOnlyDictionary<string, object?>? normalizedVars, object? originalVars)
    {
        if (result is not IQueryResult qr)
        {
            return new { error = "Unexpected GraphQL result" };
        }
        var payload = new Dictionary<string, object?>();
        if (qr.Data is not null) payload["data"] = qr.Data;
        if (qr.Errors is { Count: > 0 })
        {
            payload["errors"] = qr.Errors.Select(e => new
            {
                message = e.Message,
                code = e.Code,
                path = e.Path?.ToString(),
                extensions = e.Extensions is { Count: > 0 } ? e.Extensions : null
            });
        }
        if (qr.Extensions is not null && qr.Extensions.Count > 0) payload["extensions"] = qr.Extensions;

        if (DebugToggle.IsEnabled(http))
        {
            payload["debug"] = new
            {
                correlationId = Activity.Current?.Id,
                variablesShape = DescribeVariables(normalizedVars),
                variablesType = originalVars?.GetType().FullName
            };
        }
        return payload;
    }

    private static object DescribeVariables(IReadOnlyDictionary<string, object?>? vars)
    {
        if (vars is null) return new { present = false };
        var shape = new Dictionary<string, object?>();
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
}

