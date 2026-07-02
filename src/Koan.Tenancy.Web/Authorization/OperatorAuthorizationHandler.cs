using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Koan.Tenancy.Web.Authorization;

/// <summary>
/// The posture-aware gate for the tenancy control-plane console (ARCH-0104), mirroring the tenancy dev-open /
/// prod-closed philosophy:
/// <list type="bullet">
/// <item><b>Open (Development)</b> — the console just works; the relaxation is self-announcing (the tenancy boot
/// report already prints the Open posture). No ceremony to see the fleet in dev.</item>
/// <item><b>Closed (Production)</b> — the caller must be authenticated <b>and</b> carry the explicit host role
/// <see cref="TenancyRoles.Operator"/> (granted out-of-band; never derived from a tenant membership — the design's
/// "no master backdoor"). Absent that, the handler grants nothing, so the policy <b>fails closed</b>.</item>
/// </list>
/// Fail-closed by construction: the handler only ever calls <see cref="AuthorizationHandlerContext.Succeed"/>; a
/// caller that satisfies neither branch is denied because no requirement is marked satisfied.
/// </summary>
public sealed class OperatorAuthorizationHandler : AuthorizationHandler<OperatorRequirement>
{
    private readonly TenancyRuntime _runtime;

    public OperatorAuthorizationHandler(TenancyRuntime runtime) => _runtime = runtime;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorRequirement requirement)
    {
        if (_runtime.Posture == TenancyPosture.Open)
        {
            // Dev-open: the console just works. (Prod can never reach Open — the tenancy boot pre-flight refuses a
            // forced-Open-in-prod boot before this handler ever runs.)
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Prod-closed: require an authenticated principal carrying the explicit host operator role.
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true && user.IsInRole(TenancyRoles.Operator))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
