using Koan.Admin.Contracts;
using Koan.Admin.Services;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminRouteConvention : IApplicationModelConvention
{
    public const string RootPlaceholder = "[koan-admin-root]";
    public const string ApiPlaceholder = "[koan-admin-api]";

    private readonly IKoanAdminRouteProvider _routes;

    public KoanAdminRouteConvention(IKoanAdminRouteProvider routes)
    {
        _routes = routes;
    }

    public void Apply(ApplicationModel application)
    {
        var map = _routes.Current;
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel is { Template: { } template })
                {
                    var updated = Replace(template, map);
                    if (!string.Equals(updated, template, StringComparison.Ordinal))
                    {
                        selector.AttributeRouteModel.Template = updated;
                    }
                }
            }

            foreach (var action in controller.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    if (selector.AttributeRouteModel is { Template: { } template })
                    {
                        var updated = Replace(template, map);
                        if (!string.Equals(updated, template, StringComparison.Ordinal))
                        {
                            selector.AttributeRouteModel.Template = updated;
                        }
                    }
                }
            }
        }
    }

    private static string Replace(string template, KoanAdminRouteMap map)
    {
        var updated = template;
        if (updated.Contains(RootPlaceholder, StringComparison.Ordinal))
        {
            updated = updated.Replace(RootPlaceholder, map.RootTemplate, StringComparison.Ordinal);
        }
        if (updated.Contains(ApiPlaceholder, StringComparison.Ordinal))
        {
            updated = updated.Replace(ApiPlaceholder, map.ApiTemplate, StringComparison.Ordinal);
        }
        return updated;
    }
}
