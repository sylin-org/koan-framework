using Koan.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace SnapVault.Initialization;

/// <summary>
/// The studio-operator floor. A validated gallery context or tenant membership carrying the guest role is refused.
/// Production additionally requires an authorized ambient tenant; Development's explicit open posture retains the
/// local-first studio surface. Applied to management controllers and studio write/aggregate actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperatorOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var runtime = context.HttpContext.RequestServices.GetService<TenancyRuntime>();
        var isDevelopmentOpen = runtime?.Posture == TenancyPosture.Open;
        var isGuest = SnapVaultContextContributor.CurrentGrant(context.HttpContext) is not null
                      || context.HttpContext.User.IsInRole(Models.GalleryGrant.TenantRole);
        var hasTenantAuthority = Tenant.Current is not null || isDevelopmentOpen;
        if (isGuest || !hasTenantAuthority)
            context.Result = new ObjectResult(new { error = "Studio operator access required." })
            { StatusCode = StatusCodes.Status403Forbidden };
    }
}
