using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Contributors;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Runs <see cref="IKoanAuthFlowHandler"/>s for each lifecycle event in
/// <see cref="IKoanAuthFlowHandler.Priority"/> order, sequentially. Soft-fails per-handler on
/// exception (logs and continues); propagates <see cref="OperationCanceledException"/>.
/// </summary>
/// <remarks>
/// <para>
/// Single, shared dispatcher across every flow event (sign-in / sign-out / bootstrap /
/// validate-principal / challenge / access-denied), running each registered
/// <see cref="IKoanAuthFlowHandler"/> in <see cref="IKoanAuthFlowHandler.Priority"/> order.
/// </para>
/// <para>
/// Short-circuit policy varies per event:
/// </para>
/// <list type="bullet">
///   <item><see cref="DispatchSignIn"/>: stops when <see cref="AuthSignInContext.RejectReason"/> is set
///     by a handler (existing behavior preserved).</item>
///   <item><see cref="DispatchChallenge"/> / <see cref="DispatchAccessDenied"/>: all handlers still
///     run, but later handlers should treat the response as finalized once
///     <see cref="AuthChallengeContext.ResponseHandled"/> /
///     <see cref="AuthAccessDeniedContext.ResponseHandled"/> is set. This lets a high-priority
///     handler shape the response while still letting later handlers emit audit / metrics for
///     side effects.</item>
///   <item><see cref="DispatchValidatePrincipal"/>: no short-circuit; every handler observes the
///     same <see cref="AuthValidatePrincipalContext"/>.</item>
/// </list>
/// </remarks>
public sealed class AuthFlowDispatcher
{
    private readonly IReadOnlyList<IKoanAuthFlowHandler> _handlers;
    private readonly ILogger<AuthFlowDispatcher> _logger;

    public AuthFlowDispatcher(
        IEnumerable<IKoanAuthFlowHandler> handlers,
        ILogger<AuthFlowDispatcher> logger)
    {
        // Stable sort: primary key is Priority; tie-break by full type name so two handlers at the
        // same priority run in deterministic order across processes / restarts.
        _handlers = handlers
            .OrderBy(h => h.Priority)
            .ThenBy(h => h.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        _logger = logger;
    }

    /// <summary>Count of registered handlers. Surfaced for diagnostics / startup logging.</summary>
    public int Count => _handlers.Count;

    public async Task DispatchBootstrap(AuthBootstrapContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            try { await h.OnBootstrap(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: bootstrap handler {Handler} failed; continuing pipeline",
                    h.GetType().FullName);
            }
        }
    }

    public async Task DispatchValidatePrincipal(AuthValidatePrincipalContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            try { await h.OnValidatePrincipal(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: validate-principal handler {Handler} failed; continuing pipeline",
                    h.GetType().FullName);
            }
        }
    }

    public async Task DispatchSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            if (ctx.RejectReason is not null) break;
            try { await h.OnSignIn(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: sign-in handler {Handler} failed for user {UserId}; continuing pipeline",
                    h.GetType().FullName, ctx.UserId);
            }
        }
    }

    public async Task DispatchSignOut(AuthSignOutContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            try { await h.OnSignOut(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: sign-out handler {Handler} failed for user {UserId}; continuing pipeline",
                    h.GetType().FullName, ctx.UserId);
            }
        }
    }

    public async Task DispatchChallenge(AuthChallengeContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            try { await h.OnChallenge(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: challenge handler {Handler} failed; continuing pipeline",
                    h.GetType().FullName);
            }
        }
    }

    public async Task DispatchAccessDenied(AuthAccessDeniedContext ctx, CancellationToken ct)
    {
        foreach (var h in _handlers)
        {
            try { await h.OnAccessDenied(ctx, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth.Flow: access-denied handler {Handler} failed; continuing pipeline",
                    h.GetType().FullName);
            }
        }
    }
}
