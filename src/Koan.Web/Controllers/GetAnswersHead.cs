using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Koan.Web.Controllers;

/// <summary>Wherever an action answers <c>GET</c> it answers <c>HEAD</c> (WEB-0091): a route that takes <c>GET</c> and not <c>HEAD</c> takes
/// <c>HEAD</c> too, in its method constraint and in its endpoint metadata, so the read runs as it does for <c>GET</c> and the server sends no
/// body. Where an action declares <c>HEAD</c> at that route itself, its template spelled as the <c>GET</c>'s is, parameter names and
/// constraints included, its own answer stands and the <c>GET</c> takes none, so for that case no request matches twice.</summary>
internal sealed class GetAnswersHead : IApplicationModelConvention, IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Conventions.Add(this);

    public void Apply(ApplicationModel application)
    {
        var routes = application.Controllers
            .SelectMany(controller => controller.Actions.SelectMany(action => action.Selectors.Select(selector => (selector, Templates(controller, selector)))))
            .ToArray();
        var headed = routes.Where(route => Takes(route.selector, HttpMethods.Head))
            .SelectMany(route => route.Item2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (selector, templates) in routes)
            if (Takes(selector, HttpMethods.Get) && !Takes(selector, HttpMethods.Head) && !templates.Any(headed.Contains))
                Head(selector);
    }

    /// <summary>The routes a selector answers at, each with its controller's route before it.</summary>
    private static string[] Templates(ControllerModel controller, SelectorModel selector)
        => (controller.Selectors.Count == 0 ? [selector.AttributeRouteModel] : controller.Selectors
                .Select(owner => AttributeRouteModel.CombineAttributeRouteModel(owner.AttributeRouteModel, selector.AttributeRouteModel)))
            .Select(route => route?.Template?.Trim('/') ?? "").ToArray();

    private static bool Takes(SelectorModel selector, string method)
        => selector.ActionConstraints.OfType<HttpMethodActionConstraint>()
            .Any(constraint => constraint.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase));

    private static void Head(SelectorModel selector)
    {
        for (var i = 0; i < selector.ActionConstraints.Count; i++)
            if (selector.ActionConstraints[i] is HttpMethodActionConstraint methods)
                selector.ActionConstraints[i] = new HttpMethodActionConstraint([.. methods.HttpMethods, HttpMethods.Head]);
        for (var i = 0; i < selector.EndpointMetadata.Count; i++)
            if (selector.EndpointMetadata[i] is HttpMethodMetadata metadata)
                selector.EndpointMetadata[i] = new HttpMethodMetadata([.. metadata.HttpMethods, HttpMethods.Head], metadata.AcceptCorsPreflight);
    }
}
