using Koan.Web.Controllers;
using Koan.Web.Filters;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Koan.Web.Attributes;

/// <summary>
/// An <see cref="EntityController{TEntity,TKey}"/> that serves what it declares (WEB-0087): only the operations it overrides are routes, and
/// tools where <c>[Tool]</c> says so; an override's own HTTP attribute is its route, in place of the one it inherits; <c>POST ""</c> only
/// creates, so a row already holding the id answers <c>409 id-taken</c>; a query or body word an operation does not take
/// (<see cref="InputAttribute{TInput}"/>) is refused with its field named; and a collection or query answers in the format the caller accepts,
/// JSON by default, so an emit hook's record reaches the application's formatter. Its collection continues from a sealed <c>after</c>, a
/// page of <c>size</c> rows, in the order its <see cref="PaginationAttribute"/>'s <c>DefaultSort</c> declares (DATA-0170), and counts nothing.
/// Its writes take a page's form as well as JSON, a form
/// naming the method it cannot send in <c>_method</c>, and answer through <see cref="Answers.Written"/> (WEB-0088).
/// </summary>
/// <example>
/// <code>
/// [Route("t/{tangent}")]
/// [Declared]
/// public sealed class TopicsController : EntityController&lt;Post&gt;
/// {
///     [HttpGet("{topic}")]
///     [Tool(Name = "read_topic")]
///     public override Task&lt;IActionResult&gt; GetById(string topic, CancellationToken ct) => base.GetById(topic, ct);
/// }
/// </code>
/// A singleton routes its key operations at <c>""</c> (<c>[HttpGet("")]</c>, <c>[HttpPatch("")]</c>) and names its one id by overriding
/// <c>KeyOf</c>.
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class DeclaredAttribute : Attribute, IControllerModelConvention
{
    void IControllerModelConvention.Apply(ControllerModel controller)
    {
        foreach (var action in controller.Actions.ToArray())
        {
            if (!EntityOperation.IsEntityOperation(action.ActionMethod)) continue;
            if (EntityOperation.IsInherited(action.ActionMethod))
            {
                controller.Actions.Remove(action);
                continue;
            }

            OwnRoute(action);
            switch (action.ActionMethod.Name)
            {
                case nameof(EntityController<,>.GetCollection):
                    // DATA-0170: its order is compiled where the routes are, so an order no list can continue through stops the host.
                    _ = EntityOperation.Of(action.ActionMethod, controller.ControllerType);
                    Negotiated(action);
                    break;
                case nameof(EntityController<,>.Query):
                    Negotiated(action);
                    break;
                case nameof(EntityController<,>.Upsert):
                    if (!action.Filters.Contains(BufferedBody.Instance)) action.Filters.Add(BufferedBody.Instance);
                    Refuses(action);
                    break;
                case nameof(EntityController<,>.Patch):
                    TakesForms(action);
                    Refuses(action);
                    break;
                case nameof(EntityController<,>.Delete):
                    Refuses(action);
                    break;
            }
        }
    }

    /// <summary>A body that cannot bind is refused as Koan refuses, and a refused write that came as a form answers with what it sent (WEB-0088).</summary>
    private static void Refuses(ActionModel action)
    {
        if (!action.Filters.Contains(RefusedWrite.Instance)) action.Filters.Add(RefusedWrite.Instance);
    }

    /// <summary>A change takes a page's form as well as JSON (WEB-0088): the one it inherits consumes JSON alone.</summary>
    private static void TakesForms(ActionModel action)
    {
        var consumes = new ConsumesAttribute(KoanWebConstants.ContentTypes.ApplicationJsonPatch, KoanWebConstants.ContentTypes.ApplicationMergePatch,
            KoanWebConstants.ContentTypes.ApplicationJson, KoanWebConstants.ContentTypes.FormUrlEncoded, KoanWebConstants.ContentTypes.MultipartForm);
        for (var i = 0; i < action.Filters.Count; i++)
            if (action.Filters[i] is ConsumesAttribute) action.Filters[i] = consumes;
        foreach (var selector in action.Selectors)
        {
            for (var i = 0; i < selector.ActionConstraints.Count; i++)
                if (selector.ActionConstraints[i] is ConsumesAttribute) selector.ActionConstraints[i] = consumes;
            for (var i = 0; i < selector.EndpointMetadata.Count; i++)
                if (selector.EndpointMetadata[i] is ConsumesAttribute) selector.EndpointMetadata[i] = consumes;
        }
    }

    /// <summary>An override that states its own route answers there alone, not also at the route it inherits.</summary>
    private static void OwnRoute(ActionModel action)
    {
        var own = action.ActionMethod.GetCustomAttributes(inherit: false).OfType<IRouteTemplateProvider>()
            .Select(provider => (provider.GetType(), provider.Template)).ToHashSet();
        if (own.Count == 0) return;
        var kept = new HashSet<(Type, string?)>();
        foreach (var selector in action.Selectors.ToArray())
        {
            var provider = selector.AttributeRouteModel?.Attribute ?? selector.EndpointMetadata.OfType<IRouteTemplateProvider>().FirstOrDefault();
            var key = provider is null ? default : (provider.GetType(), provider.Template);
            if (provider is null || !own.Contains(key) || !kept.Add(key)) action.Selectors.Remove(selector);
        }
    }

    /// <summary>A collection answers in the format the caller accepts, JSON where it names none.</summary>
    private static void Negotiated(ActionModel action)
    {
        foreach (var produces in action.Filters.OfType<ProducesAttribute>().ToArray()) action.Filters.Remove(produces);
        foreach (var selector in action.Selectors)
            foreach (var produces in selector.EndpointMetadata.OfType<ProducesAttribute>().ToArray()) selector.EndpointMetadata.Remove(produces);
    }
}
