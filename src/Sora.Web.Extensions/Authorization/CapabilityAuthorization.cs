using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Web.Extensions.Authorization;

public enum CapabilityDefaultBehavior
{
    Allow,
    Deny
}

public sealed class CapabilityPolicy
{
    public ModerationPolicy Moderation { get; set; } = new();
    public SoftDeletePolicy SoftDelete { get; set; } = new();
    public AuditPolicy Audit { get; set; } = new();
}

public sealed class ModerationPolicy
{
    public string? DraftCreate { get; set; }
    public string? DraftUpdate { get; set; }
    public string? DraftGet { get; set; }
    public string? Submit { get; set; }
    public string? Withdraw { get; set; }
    public string? Queue { get; set; }
    public string? Approve { get; set; }
    public string? Reject { get; set; }
    public string? Return { get; set; }
}

public sealed class SoftDeletePolicy
{
    public string? ListDeleted { get; set; }
    public string? Delete { get; set; }
    public string? DeleteMany { get; set; }
    public string? Restore { get; set; }
    public string? RestoreMany { get; set; }
}

public sealed class AuditPolicy
{
    public string? Snapshot { get; set; }
    public string? List { get; set; }
    public string? Revert { get; set; }
}

public sealed class CapabilityAuthorizationOptions
{
    public CapabilityDefaultBehavior DefaultBehavior { get; set; } = CapabilityDefaultBehavior.Allow;
    public CapabilityPolicy Defaults { get; set; } = new();
    public Dictionary<string, CapabilityPolicy> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface ICapabilityAuthorizer
{
    bool IsAllowed(ClaimsPrincipal user, Type entityType, string capabilityAction);
}

internal sealed class CapabilityAuthorizer : ICapabilityAuthorizer
{
    private readonly CapabilityAuthorizationOptions _options;
    private readonly IAuthorizationService _authz;

    public CapabilityAuthorizer(CapabilityAuthorizationOptions options, IAuthorizationService authz)
    {
        _options = options;
        _authz = authz;
    }

    public bool IsAllowed(ClaimsPrincipal user, Type entityType, string capabilityAction)
    {
        // Resolve mapping: Entity → Defaults → DefaultBehavior
        var entityName = entityType.Name;
        var policyName = ResolvePolicyName(entityName, capabilityAction);
        if (policyName is null)
        {
            return _options.DefaultBehavior == CapabilityDefaultBehavior.Allow;
        }

        // If a policy name is configured, use ASP.NET AuthorizationService to evaluate.
    var result = _authz.AuthorizeAsync(user, null, policyName).GetAwaiter().GetResult();
        return result.Succeeded;
    }

    private string? ResolvePolicyName(string entityName, string action)
    {
        if (_options.Entities.TryGetValue(entityName, out var entityPolicy))
        {
            var p = Pick(entityPolicy, action);
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        var d = Pick(_options.Defaults, action);
        return string.IsNullOrWhiteSpace(d) ? null : d;
    }

    private static string? Pick(CapabilityPolicy policy, string action)
    {
        return action switch
        {
            Capabilities.CapabilityActions.Moderation.DraftCreate => policy.Moderation.DraftCreate,
            Capabilities.CapabilityActions.Moderation.DraftUpdate => policy.Moderation.DraftUpdate,
            Capabilities.CapabilityActions.Moderation.DraftGet => policy.Moderation.DraftGet,
            Capabilities.CapabilityActions.Moderation.Submit => policy.Moderation.Submit,
            Capabilities.CapabilityActions.Moderation.Withdraw => policy.Moderation.Withdraw,
            Capabilities.CapabilityActions.Moderation.Queue => policy.Moderation.Queue,
            Capabilities.CapabilityActions.Moderation.Approve => policy.Moderation.Approve,
            Capabilities.CapabilityActions.Moderation.Reject => policy.Moderation.Reject,
            Capabilities.CapabilityActions.Moderation.Return => policy.Moderation.Return,

            Capabilities.CapabilityActions.SoftDelete.ListDeleted => policy.SoftDelete.ListDeleted,
            Capabilities.CapabilityActions.SoftDelete.Delete => policy.SoftDelete.Delete,
            Capabilities.CapabilityActions.SoftDelete.DeleteMany => policy.SoftDelete.DeleteMany,
            Capabilities.CapabilityActions.SoftDelete.Restore => policy.SoftDelete.Restore,
            Capabilities.CapabilityActions.SoftDelete.RestoreMany => policy.SoftDelete.RestoreMany,

            Capabilities.CapabilityActions.Audit.Snapshot => policy.Audit.Snapshot,
            Capabilities.CapabilityActions.Audit.List => policy.Audit.List,
            Capabilities.CapabilityActions.Audit.Revert => policy.Audit.Revert,

            _ => null
        };
    }
}

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

public static class CapabilityAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityAuthorization(this IServiceCollection services, Action<CapabilityAuthorizationOptions> configure)
    {
        var opts = new CapabilityAuthorizationOptions();
        configure?.Invoke(opts);
        services.AddSingleton(opts);
        services.AddScoped<ICapabilityAuthorizer, CapabilityAuthorizer>();
        return services;
    }
}
