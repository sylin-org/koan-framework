using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Koan.Web.Controllers;

/// <summary>An <see cref="EntityController{TEntity,TKey}"/> that is a hook of its own requests (WEB-0087) answers no request through its hook
/// methods: each is a step of the controller's operations, never a route of its own.</summary>
internal sealed class EntityHooksAreNotActions : IApplicationModelConvention, IConfigureOptions<MvcOptions>
{
    private static readonly Type[] Hooks = [typeof(IModelHook<>), typeof(IRequestOptionsHook<>), typeof(ICollectionHook<>), typeof(IEmitHook<>)];

    public void Configure(MvcOptions options) => options.Conventions.Add(this);

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var steps = controller.ControllerType.GetInterfaces()
                .Where(contract => contract == typeof(IOrderedHook) || contract.IsGenericType && Hooks.Contains(contract.GetGenericTypeDefinition()))
                .SelectMany(contract => controller.ControllerType.GetInterfaceMap(contract).TargetMethods)
                .ToHashSet();
            if (steps.Count == 0) continue;
            foreach (var action in controller.Actions.Where(action => steps.Contains(action.ActionMethod)).ToArray())
                controller.Actions.Remove(action);
        }
    }
}
