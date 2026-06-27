using Microsoft.Extensions.Options;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// The <c>header</c> tenant carrier — reads a tenant id or <c>TenantRecord.Code</c> from a configured request header
/// (e.g. an API client passing <c>X-Koan-Tenant</c>). Client-asserted, so the middleware's membership-authorization
/// is load-bearing: a forged header can never scope a non-member into a tenant. Returns null when the header is absent.
/// </summary>
internal sealed class HeaderTenantResolver(IOptions<TenancyResolutionOptions> options) : ITenantResolver
{
    private readonly TenancyResolutionOptions _options = options.Value;

    public string Name => "header";

    public async ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
        => await TenantCodeResolver.ResolveToTenantIdAsync(request.Header(_options.HeaderName), ct).ConfigureAwait(false);
}
