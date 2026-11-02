using System.Reflection;
using Koan.Web.Controllers;
using Koan.Web.Filters;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Koan.Web.Attributes;

/// <summary>
/// One definition, two doors (ARCH-0141): a route that is also a tool where the MCP module is present. On a controller it
/// exposes every action that controller answers, inherited ones included; on an action, that action. The action stays the only definition: a tool
/// call is a request to it through the application's whole pipeline, so input, gate, refusals and effect cannot differ. With no MCP module the route is
/// all there is. A command is never a tool: only what already answers at a route can be one.
/// </summary>
/// <remarks>
/// The tool's name is the route's literal segments, then the particle or else the HTTP method, in lower-case words joined
/// by underscores: <c>[Route("/data/posts")]</c>, <c>[HttpGet("{id}")]</c> and <c>[Tool(particle: "getById")]</c> make
/// <c>data_posts_get_by_id</c>. Two tools of one name stop the host at start.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAttribute(string? particle = null) : Attribute, IControllerModelConvention, IActionModelConvention
{
    /// <summary>What ends the derived name in place of the HTTP method.</summary>
    public string? Particle { get; } = particle;

    /// <summary>The whole name, stated, in place of the derived one.</summary>
    public string? Name { get; set; }

    /// <summary>Whether <paramref name="action"/> of <paramref name="controller"/> is a tool. An action inherited from
    /// <see cref="EntityController{TEntity,TKey}"/> is not: its operations are the entity's own tools.</summary>
    public static bool Exposes(MethodInfo action, Type controller)
        => action.IsDefined(typeof(ToolAttribute), inherit: false)
           || (controller.IsDefined(typeof(ToolAttribute), inherit: false) && !IsEntityOperation(action));

    private static bool IsEntityOperation(MethodInfo action)
        => action.GetBaseDefinition().DeclaringType is { IsGenericType: true } declaring
           && declaring.GetGenericTypeDefinition() == typeof(EntityController<,>);

    void IControllerModelConvention.Apply(ControllerModel controller)
    {
        foreach (var action in controller.Actions)
            if (Exposes(action.ActionMethod, controller.ControllerType)) Filter(action);
    }

    void IActionModelConvention.Apply(ActionModel action) => Filter(action);

    private static void Filter(ActionModel action)
    {
        if (!action.Filters.Contains(ToolFilter.Instance)) action.Filters.Add(ToolFilter.Instance);
    }
}
