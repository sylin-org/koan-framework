using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Web.GraphQl.Infrastructure;

internal static class DebugToggle
{
    public static bool IsEnabled(HttpContext http)
    {
        if (http is null) return false;
        if (http.Request.Headers.TryGetValue(GraphQlConstants.DebugHeader, out var hv))
        {
            var s = hv.ToString();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        }
        var opts = http.RequestServices?.GetService(typeof(IOptions<GraphQlOptions>)) as IOptions<GraphQlOptions>;
        if (opts?.Value?.Debug == true) return true;
        return false;
    }
}
