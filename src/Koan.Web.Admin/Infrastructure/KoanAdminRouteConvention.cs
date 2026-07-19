using System;
using Koan.Web.Admin.Contracts;
using Koan.Web.Admin.Options;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminRouteConvention : IApplicationModelConvention
{
    public const string RootPlaceholder = "[koan-admin-root]";

    private readonly KoanAdminRouteMap _routes;

    public KoanAdminRouteConvention(IOptions<KoanAdminOptions> options)
    {
        _routes = KoanAdminPathUtility.BuildMap(options.Value.PathPrefix);
    }

    public void Apply(ApplicationModel application)
    {
        var map = _routes;
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
        return updated;
    }
}
