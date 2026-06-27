using Microsoft.Extensions.Options;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// The <c>path</c> tenant carrier — reads the segment after a configured prefix as a <c>TenantRecord.Code</c> (e.g.
/// prefix <c>/t/</c> + path <c>/t/acme/orders</c> → code <c>acme</c>). Client-asserted, so the middleware's
/// membership-authorization is load-bearing.
/// </summary>
internal sealed class PathTenantResolver(IOptions<TenancyResolutionOptions> options) : ITenantResolver
{
    private readonly TenancyResolutionOptions _options = options.Value;

    public string Name => "path";

    public async ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
    {
        var code = ExtractCode(request.Path, _options.PathPrefix);
        return code is null ? null : await TenantCodeResolver.ResolveToTenantIdAsync(code, ct).ConfigureAwait(false);
    }

    /// <summary>Return the segment after <paramref name="prefix"/>, or null. Pure for unit testing.</summary>
    internal static string? ExtractCode(string? path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(prefix)) return null;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/');
        var segment = slash < 0 ? rest : rest[..slash];
        return string.IsNullOrWhiteSpace(segment) ? null : segment;
    }
}
