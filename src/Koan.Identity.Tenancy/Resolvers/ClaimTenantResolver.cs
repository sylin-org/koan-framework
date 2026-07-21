using Microsoft.Extensions.Options;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// The <c>claim</c> tenant carrier — reads a tenant id from a configured claim on the authenticated principal (the
/// most trusted carrier: minted at sign-in once a tenant is selected). Still membership-authorized by the middleware
/// (defense in depth). Returns null when the claim is absent.
/// </summary>
internal sealed class ClaimTenantResolver(IOptions<TenancyResolutionOptions> options) : ITenantResolver
{
    private readonly TenancyResolutionOptions _options = options.Value;

    public string Name => "claim";

    public async ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
        => await TenantCodeResolver.ResolveToTenantIdAsync(request.Claim(_options.ClaimType), ct).ConfigureAwait(false);
}
