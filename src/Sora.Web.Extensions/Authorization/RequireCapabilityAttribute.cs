using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Web.Extensions.Authorization;

/// <summary>
/// Attribute to enforce capability authorization on capability controller actions.
/// It uses the per-app CapabilityAuthorizationOptions resolution.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _action;
    public RequireCapabilityAttribute(string capabilityAction)
    {
        _action = capabilityAction;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authorizer = context.HttpContext.RequestServices.GetService<ICapabilityAuthorizer>();
        var opts = context.HttpContext.RequestServices.GetService<CapabilityAuthorizationOptions>();

        if (authorizer is null || opts is null)
        {
            // No capability policy registered — allow by default for backward-compat.
            return Task.CompletedTask;
        }

        // Discover the entity generic argument from the controller descriptor
        var cad = context.ActionDescriptor as ControllerActionDescriptor;
        var ctrlType = cad?.ControllerTypeInfo?.AsType() ?? typeof(object);
        var entityType = ctrlType.IsConstructedGenericType ? ctrlType.GetGenericArguments()[0] : ctrlType;

        var user = context.HttpContext.User;
        var allowed = authorizer.IsAllowed(user, entityType, _action);
        if (!allowed)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = $"Capability '{_action}' denied by policy for entity '{entityType.Name}'."
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return Task.CompletedTask;
    }
}