using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Contributors;

/// <summary>
/// Runs <see cref="IKoanAuthEventContributor"/>s for each lifecycle event in
/// <see cref="IKoanAuthEventContributor.Priority"/> order. Soft-fails per-contributor on
/// exception (logs and continues); propagates <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class AuthEventDispatcher
{
    private readonly IReadOnlyList<IKoanAuthEventContributor> _contributors;
    private readonly ILogger<AuthEventDispatcher> _logger;

    public AuthEventDispatcher(IEnumerable<IKoanAuthEventContributor> contributors, ILogger<AuthEventDispatcher> logger)
    {
        _contributors = contributors.OrderBy(c => c.Priority).ToArray();
        _logger = logger;
    }

    /// <summary>Number of registered contributors. Surfaced for diagnostics / startup logging.</summary>
    public int Count => _contributors.Count;

    public async Task DispatchBootstrap(AuthBootstrapContext ctx, CancellationToken ct)
    {
        foreach (var c in _contributors)
        {
            try
            {
                await c.OnBootstrap(ctx, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth: bootstrap contributor {Contributor} failed; continuing pipeline",
                    c.GetType().FullName);
            }
        }
    }

    public async Task DispatchSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        foreach (var c in _contributors)
        {
            if (ctx.RejectReason is not null) break;

            try
            {
                await c.OnSignIn(ctx, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth: sign-in contributor {Contributor} failed for user {UserId}; continuing pipeline",
                    c.GetType().FullName, ctx.UserId);
            }
        }
    }

    public async Task DispatchSignOut(AuthSignOutContext ctx, CancellationToken ct)
    {
        foreach (var c in _contributors)
        {
            try
            {
                await c.OnSignOut(ctx, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Koan.Web.Auth: sign-out contributor {Contributor} failed for user {UserId}; continuing pipeline",
                    c.GetType().FullName, ctx.UserId);
            }
        }
    }
}
