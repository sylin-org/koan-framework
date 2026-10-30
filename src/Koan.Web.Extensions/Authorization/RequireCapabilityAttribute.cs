using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Hooks;

using Koan.Web.Authorization;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// Enforces capability authorization on capability controller actions through the unified SEC-0002
/// <see cref="IAuthorize"/> seam: the capability action + the controller's entity type become an
/// <see cref="AuthorizeRequest"/>, which the <see cref="PolicyAuthorizationProvider"/> resolves via the
/// WEB-0047 (Entity → Defaults → DefaultBehavior) logic. The module registers that provider with the reference, so an
/// action with no policy is denied (WEB-0083). Truly async — no sync-over-async.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _action;

    public RequireCapabilityAttribute(string capabilityAction)
    {
        _action = capabilityAction;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authorize = context.HttpContext.RequestServices.GetRequiredService<IAuthorize>();

        var cad = context.ActionDescriptor as ControllerActionDescriptor;
        var entityType = EntityOf(cad?.ControllerTypeInfo?.AsType() ?? typeof(object));

        var decision = await authorize.AuthorizeAsync(new AuthorizeRequest
        {
            Subject = context.HttpContext.User,
            Action = _action,
            Resource = entityType,
        });

        // Capability gates never Challenge (the policy provider returns Allow/Forbid) — treat non-Allow as 403.
        if (decision is not AuthorizeDecision.Allow)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = (decision as AuthorizeDecision.Forbid)?.Reason
                    ?? $"Capability '{_action}' denied by policy for entity '{entityType.Name}'."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    // The entity a capability controller governs: the first generic argument of the nearest closed base, since an
    // application's `TodoAuditController : EntityAuditController<Todo>` is not itself generic and policy maps by entity.
    private static Type EntityOf(Type controller)
    {
        for (var type = controller; type is not null; type = type.BaseType)
        {
            if (type.IsConstructedGenericType) return type.GetGenericArguments()[0];
        }
        return controller;
    }
}
