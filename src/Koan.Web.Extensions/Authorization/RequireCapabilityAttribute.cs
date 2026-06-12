using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Koan.Web.Hooks;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// Enforces capability authorization on capability controller actions through the unified SEC-0002
/// <see cref="IAuthorize"/> seam: the capability action + the controller's entity type become an
/// <see cref="AuthorizeRequest"/>, which the <see cref="PolicyAuthorizationProvider"/> resolves via the
/// WEB-0047 (Entity → Defaults → DefaultBehavior) logic. Truly async — no sync-over-async.
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
        var authorize = context.HttpContext.RequestServices.GetService<IAuthorize>();
        if (authorize is null)
            return; // no authorization registered → allow by default (backward-compat)

        // Discover the entity generic argument from the controller descriptor.
        var cad = context.ActionDescriptor as ControllerActionDescriptor;
        var ctrlType = cad?.ControllerTypeInfo?.AsType() ?? typeof(object);
        var entityType = ctrlType.IsConstructedGenericType ? ctrlType.GetGenericArguments()[0] : ctrlType;

        var decision = await authorize.AuthorizeAsync(new AuthorizeRequest
        {
            Subject = context.HttpContext.User,
            Action = _action,
            Resource = entityType,
        });

        // Capability gates never Challenge (the policy provider returns Allow/Forbid) — treat non-Allow as 403,
        // preserving the original allow/deny semantics.
        if (decision is not AuthorizeDecision.Allow)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = $"Capability '{_action}' denied by policy for entity '{entityType.Name}'."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
