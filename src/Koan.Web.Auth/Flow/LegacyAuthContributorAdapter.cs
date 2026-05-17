using Koan.Web.Auth.Contributors;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Bridges an <see cref="IKoanAuthEventContributor"/> implementation into the new
/// <see cref="IKoanAuthFlowHandler"/> dispatcher pipeline so existing contributors keep working
/// without code changes. One adapter per contributor; the framework registers them automatically
/// during service-collection setup so consumers never see them.
/// </summary>
/// <remarks>
/// <para>
/// Each adapter forwards the legacy bootstrap / sign-in / sign-out callbacks verbatim, and
/// no-ops on the new events (<see cref="OnValidatePrincipal"/>, <see cref="OnChallenge"/>,
/// <see cref="OnAccessDenied"/>) that the legacy contract didn't expose. Priority is mirrored
/// 1:1 from the wrapped contributor so the dispatch order is preserved when both legacy and new
/// handlers are mixed in the same pipeline.
/// </para>
/// <para>
/// Migration path: implement <see cref="IKoanAuthFlowHandler"/> directly, then remove the
/// <see cref="IKoanAuthEventContributor"/> implementation. The new interface subsumes everything
/// the old one carried and adds the new lifecycle events.
/// </para>
/// </remarks>
internal sealed class LegacyAuthContributorAdapter : IKoanAuthFlowHandler
{
    private readonly IKoanAuthEventContributor _inner;

    public LegacyAuthContributorAdapter(IKoanAuthEventContributor inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public int Priority => _inner.Priority;

    public Task OnBootstrap(AuthBootstrapContext ctx, CancellationToken ct) => _inner.OnBootstrap(ctx, ct);
    public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct) => _inner.OnSignIn(ctx, ct);
    public Task OnSignOut(AuthSignOutContext ctx, CancellationToken ct) => _inner.OnSignOut(ctx, ct);

    /// <summary>Identity of the wrapped contributor — useful for log-line attribution in the dispatcher.</summary>
    internal Type WrappedType => _inner.GetType();
}
