using Microsoft.Extensions.Options;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// The <c>claim</c> tenant carrier — reads a tenant id from a configured claim on the authenticated principal: the
/// session's standing default, minted at sign-in once a tenant is selected, which the address and a request's header
/// speak over. Membership-authorized by the middleware like every carrier. Returns null when the claim is absent.
/// </summary>
internal sealed class ClaimTenantResolver(IOptions<TenancyResolutionOptions> options) : ITenantResolver
{
    private readonly TenancyResolutionOptions _options = options.Value;

    public string Name => "claim";

    public TenantCarrierRank Rank => TenantCarrierRank.Session;

    public async ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
        => await TenantCodeResolver.ResolveToTenantIdAsync(request.Claim(_options.ClaimType), ct).ConfigureAwait(false);
}
