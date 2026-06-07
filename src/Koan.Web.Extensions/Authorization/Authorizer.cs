using Microsoft.Extensions.Options;
using Koan.Web.Hooks;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0002 — the seam implementation. Runs the <see cref="IAuthorizationProvider"/> ladder in <c>Order</c>;
/// the first non-<c>null</c> provider decision wins; if every provider defers, the configured default
/// behavior applies.
/// </summary>
internal sealed class Authorizer : IAuthorize
{
    private readonly IAuthorizationProvider[] _providers;
    private readonly AuthorizeOptions _options;

    public Authorizer(IEnumerable<IAuthorizationProvider> providers, IOptions<AuthorizeOptions> options)
    {
        _providers = providers.OrderBy(p => p.Order).ToArray();
        _options = options.Value;
    }

    public async Task<AuthorizeDecision> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var decision = await provider.EvaluateAsync(request, ct).ConfigureAwait(false);
            if (decision is not null) return decision;
        }

        return _options.DefaultDecision == AuthorizeDefault.Allow
            ? AuthorizeDecision.Allowed()
            : AuthorizeDecision.Forbidden("no authorization provider granted access");
    }
}
