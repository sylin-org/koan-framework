using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Sora.Web.GraphQl.Infrastructure;

internal static class DebugToggle
{
    public static bool IsEnabled(HttpContext http)
    {
        var cfg = http?.RequestServices?.GetService(typeof(IConfiguration)) as IConfiguration;
        return IsEnabled(http!, cfg);
    }

    public static bool IsEnabled(HttpContext http, IConfiguration? cfg)
    {
        if (http is null) return false;
        if (http.Request.Headers.TryGetValue(GraphQlConstants.DebugHeader, out var hv))
        {
            var s = hv.ToString();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        }
        var v = cfg?["Sora:GraphQl:Debug"];
        if (!string.IsNullOrWhiteSpace(v) && (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1")) return true;
        return false;
    }
}
