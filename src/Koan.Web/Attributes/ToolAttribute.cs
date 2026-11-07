using System.Reflection;
using Koan.Web.Authorization;
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

    /// <summary>Whether a call may destroy something, such as a <c>POST</c> that removes or bans, so a client asks its person first; false says it only adds. Left unsaid, a
    /// <c>DELETE</c> does, a read does not, and of anything else nothing is said, which the specification reads as "it may". On a controller it speaks for every action that does not say.</summary>
    public bool Destructive { get => _destructive ?? false; set => _destructive = value; }

    /// <summary>Whether calling again with the same arguments changes nothing more, so a client may retry. Left unsaid, what HTTP promises of the method:
    /// <c>GET</c>, <c>HEAD</c>, <c>PUT</c> and <c>DELETE</c> are. On a controller it speaks for every action that does not say.</summary>
    public bool Idempotent { get => _idempotent ?? false; set => _idempotent = value; }

    /// <summary>Whether a call reaches beyond this application, such as one that posts elsewhere or fetches a stranger's address; false says it stays within. Left unsaid, a read does
    /// not, and of anything else nothing is said, which the specification reads as "it may". On a controller it speaks for every action that does not say.</summary>
    public bool OpenWorld { get => _openWorld ?? false; set => _openWorld = value; }

    /// <summary>What this attribute says of a call's effects, null where it says nothing.</summary>
    public (bool? Destructive, bool? Idempotent, bool? OpenWorld) Stated => (_destructive, _idempotent, _openWorld);

    private bool? _destructive;
    private bool? _idempotent;
    private bool? _openWorld;

    /// <summary>Whether <paramref name="action"/> of <paramref name="controller"/> is a tool. An action inherited from
    /// <see cref="EntityController{TEntity,TKey}"/> is not: its operations are the entity's own tools. On a
    /// <see cref="DeclaredAttribute">declared</see> controller its overrides are its own actions, so <c>[Tool]</c> on the class exposes them
    /// too, and the entity has no tools of its own there (WEB-0087).</summary>
    public static bool Exposes(MethodInfo action, Type controller)
        => action.IsDefined(typeof(ToolAttribute), inherit: false)
           || (controller.IsDefined(typeof(ToolAttribute), inherit: false)
               && (!EntityOperation.IsEntityOperation(action) || EntityOperation.IsDeclared(controller) && !EntityOperation.IsInherited(action)));

    void IControllerModelConvention.Apply(ControllerModel controller)
    {
        OneDefinition(controller.ControllerType);
        foreach (var action in controller.Actions)
            if (Exposes(action.ActionMethod, controller.ControllerType)) Filter(action);
    }

    void IActionModelConvention.Apply(ActionModel action) => Filter(action);

    /// <summary>
    /// An undeclared <see cref="EntityController{TEntity,TKey}"/> carrying <c>[Tool]</c> is answered on MCP by its entity's own tools, which
    /// run the endpoint and never the controller (ARCH-0141). A hook the controller is, or a gate one of its overrides declares, would therefore
    /// hold on REST alone, so such a controller stops the host where its routes are composed (WEB-0087), and the two doors never differ.
    /// </summary>
    private static void OneDefinition(Type controller)
    {
        if (EntityOperation.IsDeclared(controller)) return;
        var operations = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(EntityOperation.IsEntityOperation).ToArray();
        if (operations.Length == 0) return;
        var skipped = EntityHooksAreNotActions.Contracts(controller).Any()
            ? "never run it as a hook"
            : operations.FirstOrDefault(operation => !EntityOperation.IsInherited(operation) && RouteAccess.Gates(operation)) is { } gated
                ? $"never judge the gate on its override of {gated.Name}"
                : null;
        if (skipped is not null)
            throw new InvalidOperationException($"{controller.Name} carries [Tool] without [Declared], so its entity's own tools answer for it on MCP, and they {skipped}. " +
                "Mark it [Declared] so its overrides are its tools and both doors run the same, or put the rule on the entity.");
    }

    private static void Filter(ActionModel action)
    {
        GateFilter.Convention.On(action);
        if (!action.Filters.Contains(ToolFilter.Instance)) action.Filters.Add(ToolFilter.Instance);
    }
}
