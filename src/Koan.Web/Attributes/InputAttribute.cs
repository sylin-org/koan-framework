using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Koan.Web.Attributes;

/// <summary>
/// What an operation of a <see cref="DeclaredAttribute">declared</see> controller takes beside the entity's members (WEB-0087): the members of
/// <typeparamref name="TInput"/>, read from the query and, on a write, from the same body, each under its wire name. A value that cannot be one
/// is refused with its field named, the route tool's schema lists them, and a hook reads them typed through
/// <see cref="Hooks.HookContext{TEntity}.Input{TInput}"/>. A member named after one of the endpoint's own words (<c>page</c>, <c>with</c>, ...)
/// reaches the endpoint as well.
/// </summary>
/// <example><c>[Input&lt;Audience&gt;]</c> with <c>public sealed record Audience(string? Audience);</c></example>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class InputAttribute<TInput> : Attribute, IInputAttribute, IActionModelConvention where TInput : class
{
    Type IInputAttribute.Type => typeof(TInput);

    void IActionModelConvention.Apply(ActionModel action) => WritesAttribute.Placed(action, "[Input]", write: false);
}

/// <summary>The input type an <see cref="InputAttribute{TInput}"/> declares, for a reader that does not know it.</summary>
internal interface IInputAttribute
{
    Type Type { get; }
}
