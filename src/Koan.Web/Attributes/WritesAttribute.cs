using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Koan.Web.Attributes;

/// <summary>
/// The members an entity write writes (WEB-0087), on the <c>Upsert</c> or <c>Patch</c> override of a <see cref="DeclaredAttribute">declared</see>
/// controller. A body that names any other member is refused with that field named; a member it leaves out keeps its value on <c>PATCH</c>,
/// and its default or stamped value on <c>POST</c>; record access admits the members the body names, so an <c>[Access]</c> member it leaves
/// alone refuses nothing. The route tool's schema lists these members and no others.
/// </summary>
/// <example><c>[Writes(nameof(Post.Title), nameof(Post.Body))]</c></example>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class WritesAttribute(params string[] members) : Attribute, IActionModelConvention
{
    /// <summary>The members, by their names in code.</summary>
    public IReadOnlyList<string> Members { get; } = members;

    void IActionModelConvention.Apply(ActionModel action) => Placed(action, "[Writes]", write: true);

    /// <summary>A declaration of what an operation takes holds only on a declared controller's write or operation, so it is never silently ignored.</summary>
    internal static void Placed(ActionModel action, string declaration, bool write)
    {
        var where = $"{action.Controller.ControllerType.Name}.{action.ActionMethod.Name}";
        if (!EntityOperation.IsEntityOperation(action.ActionMethod) || !EntityOperation.IsDeclared(action.Controller.ControllerType))
            throw new InvalidOperationException($"{declaration} on {where} is read only on an operation of a [Declared] EntityController. Declare the controller, or drop it.");
        if (write && action.ActionMethod.Name is not (nameof(EntityController<,>.Upsert) or nameof(EntityController<,>.Patch)))
            throw new InvalidOperationException($"{declaration} on {where} is read only on Upsert or Patch, the writes that take a body of the entity's members.");
        _ = EntityOperation.Of(action.ActionMethod, action.Controller.ControllerType);
    }
}
