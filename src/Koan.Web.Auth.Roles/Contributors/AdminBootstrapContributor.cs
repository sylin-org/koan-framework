using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Roles.Contracts;

namespace Koan.Web.Auth.Roles.Contributors;

/// <summary>
/// Built-in <see cref="IKoanAuthFlowHandler"/> that performs one-shot admin elevation on
/// sign-in according to <see cref="AuthLifecycleOptions.AdminBootstrapOptions"/>. Replaces the
/// previous in-line bootstrap path in <c>DefaultRoleAttributionService.TryApplyBootstrap</c>.
/// </summary>
/// <remarks>
/// <para>
/// Modes (configured at <c>Koan:Web:Auth:Lifecycle:AdminBootstrap:Mode</c>):
/// <list type="bullet">
/// <item><c>None</c> (default): contributor is a no-op.</item>
/// <item><c>FirstUser</c>: the first authenticated user to reach this contributor gets the
/// <c>admin</c> role. Persisted via <see cref="IRoleBootstrapStateStore"/> so it fires exactly
/// once across the cluster.</item>
/// <item><c>ClaimMatch</c>: grants <c>admin</c> when a configured claim matches one of
/// <see cref="AuthLifecycleOptions.AdminBootstrapOptions.ClaimValues"/> (or the convenience
/// <see cref="AuthLifecycleOptions.AdminBootstrapOptions.AdminEmails"/> list against
/// <see cref="ClaimTypes.Email"/>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Priority</b> is positive (100) so this runs late in the pipeline — after authoritative
/// role sources have stamped their claims and after <see cref="Builtin.RoleListFileContributor"/>
/// applied its overrides. If <c>admin</c> already landed on the principal via any earlier
/// contributor, this contributor is a no-op for that sign-in.
/// </para>
/// </remarks>
public sealed class AdminBootstrapContributor : IKoanAuthFlowHandler
{
    public int Priority => 100;

    private readonly IOptionsMonitor<AuthLifecycleOptions> _options;
    private readonly IRoleBootstrapStateStore _state;
    private readonly ILogger<AdminBootstrapContributor> _logger;

    public AdminBootstrapContributor(
        IOptionsMonitor<AuthLifecycleOptions> options,
        IRoleBootstrapStateStore state,
        ILogger<AdminBootstrapContributor> logger)
    {
        _options = options;
        _state = state;
        _logger = logger;
    }

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        var cfg = _options.CurrentValue.AdminBootstrap;
        var mode = cfg?.Mode ?? "None";
        if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase)) return;

        // Already done — never elevate twice.
        try { if (await _state.IsAdminBootstrapped(ct)) return; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Web.Auth.Roles: AdminBootstrap state lookup failed; skipping for {UserId}", ctx.UserId);
            return;
        }

        // Already has admin from an earlier contributor — no-op (and don't burn the one-shot).
        if (ctx.Identity.HasClaim(ClaimTypes.Role, "admin")) return;

        var shouldElevate = false;
        if (string.Equals(mode, "FirstUser", StringComparison.OrdinalIgnoreCase))
        {
            shouldElevate = true;
        }
        else if (string.Equals(mode, "ClaimMatch", StringComparison.OrdinalIgnoreCase))
        {
            var claimType = cfg?.ClaimType ?? ClaimTypes.Email;
            var values = new HashSet<string>(cfg?.ClaimValues ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var c in ctx.Identity.FindAll(claimType))
            {
                if (!string.IsNullOrWhiteSpace(c.Value) && values.Contains(c.Value))
                {
                    shouldElevate = true;
                    break;
                }
            }
            if (!shouldElevate && (cfg?.AdminEmails?.Length ?? 0) > 0)
            {
                var email = ctx.Identity.FindFirst(ClaimTypes.Email)?.Value;
                if (!string.IsNullOrWhiteSpace(email) &&
                    cfg!.AdminEmails!.Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase)))
                {
                    shouldElevate = true;
                }
            }
        }

        if (!shouldElevate) return;

        try
        {
            await _state.MarkAdminBootstrapped(ctx.UserId, mode, ct);
            ctx.Identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
            _logger.LogInformation(
                "Koan.Web.Auth.Roles: admin bootstrap applied for {UserId} via {Mode}",
                ctx.UserId, mode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Koan.Web.Auth.Roles: failed to persist admin bootstrap state for {UserId}; skipping elevation",
                ctx.UserId);
        }
    }
}
