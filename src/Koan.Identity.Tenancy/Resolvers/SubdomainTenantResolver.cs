using Microsoft.Extensions.Options;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// The <c>subdomain</c> tenant carrier — reads the single leading host label as a <c>TenantRecord.Code</c> when the
/// host is a configured base host (e.g. base <c>app.example.com</c> + host <c>acme.app.example.com</c> → code
/// <c>acme</c>). Inert when no base hosts are configured (it cannot tell the app suffix from the tenant label).
/// </summary>
internal sealed class SubdomainTenantResolver(IOptions<TenancyResolutionOptions> options) : ITenantResolver
{
    private readonly TenancyResolutionOptions _options = options.Value;

    public string Name => "subdomain";

    public async ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
    {
        var code = ExtractCode(request.Host, _options.BaseHosts);
        return code is null ? null : await TenantCodeResolver.ResolveToTenantIdAsync(code, ct).ConfigureAwait(false);
    }

    /// <summary>Strip a configured base host and return the single leading label, or null. Pure for unit testing.</summary>
    internal static string? ExtractCode(string? host, IList<string> baseHosts)
    {
        if (string.IsNullOrWhiteSpace(host) || baseHosts is null) return null;
        foreach (var baseHost in baseHosts)
        {
            if (string.IsNullOrWhiteSpace(baseHost)) continue;
            var suffix = "." + baseHost.Trim().TrimStart('.');
            if (host.Length <= suffix.Length || !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var label = host[..^suffix.Length];
            // Only a single leading label is a tenant code — "a.b.app.example.com" is not a tenant "a.b".
            if (label.Length > 0 && !label.Contains('.')) return label;
        }
        return null;
    }
}
