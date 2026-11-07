using System.Reflection;
using Koan.Core.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Authorization;

/// <summary>ARCH-0146 — what a routed action declares of who may reach it, read from the action and the class it is reached through: a gate,
/// which is <see cref="AccessAttribute"/>, <see cref="RequireScopeAttribute"/> or an application's <see cref="AccessAttribute{TNeed}"/>; or
/// <see cref="OpenAttribute"/>; or nothing.</summary>
internal static class RouteAccess
{
    /// <summary>Whether <paramref name="action"/>, reached through <paramref name="controller"/>, declares a gate, so it is judged before
    /// anything is bound. An operation an <see cref="EntityController{TEntity,TKey}"/> answers is judged by its entity's own gate, and also by a
    /// gate its override declares, or, on a declared controller, its class (WEB-0087), whether or not it is a tool.</summary>
    public static bool Judged(MethodInfo action, Type controller)
        => IsEntityOperation(action)
            ? Gates(action) || EntityOperation.IsDeclared(controller) && Gates(controller)
            : Gates(action) || Gates(controller);

    /// <summary>Whether <paramref name="action"/> says who may reach it: a gate, or <see cref="OpenAttribute"/>.</summary>
    public static bool Declared(MethodInfo action, Type controller)
        => IsEntityOperation(action) || Gates(action) || Gates(controller)
           || action.IsDefined(typeof(OpenAttribute), inherit: true) || controller.IsDefined(typeof(OpenAttribute), inherit: true);

    /// <summary>Whether <paramref name="member"/> itself declares a gate.</summary>
    public static bool Gates(MemberInfo member)
        => member.IsDefined(typeof(AccessAttribute), inherit: true)
           || member.IsDefined(typeof(RequireScopeAttribute), inherit: true)
           || member.GetCustomAttributes(inherit: true).Any(attribute => Typed(attribute.GetType()));

    private static bool Typed(Type? type)
    {
        for (; type is not null && type != typeof(Attribute); type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AccessAttribute<>)) return true;
        return false;
    }

    private static bool IsEntityOperation(MethodInfo action) => EntityOperation.IsEntityOperation(action);
}

/// <summary>ARCH-0146 — stops the host at start, where the application turned it on (<see cref="AuthorizeOptions.RoutesDeclareAccess"/>), over
/// a routed action of its own that says nothing of who may reach it, since a route that forgets its gate would answer everyone. A controller a
/// Koan module brings, whose assembly names its module identity, answers for itself.</summary>
internal sealed class RouteAccessStartCheck(IOptions<AuthorizeOptions> options, IActionDescriptorCollectionProvider actions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RoutesDeclareAccess) return Task.CompletedTask;
        var undeclared = actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(action => !IsKoanModule(action.ControllerTypeInfo.Assembly)
                             && !RouteAccess.Declared(action.MethodInfo, action.ControllerTypeInfo.AsType()))
            .Select(action => $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (undeclared.Length > 0)
            throw new InvalidOperationException($"{string.Join(", ", undeclared)} say nothing of who may reach them, so each would answer everyone. " +
                "Declare a gate on the action or its class, such as [Access(...)] or your own AccessAttribute<TNeed>, or [Open(\"why\")] where nothing judges it.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsKoanModule(Assembly assembly)
        => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(metadata => metadata.Key == Constants.Composition.ModuleIdentityMetadataName && !string.IsNullOrEmpty(metadata.Value));
}
