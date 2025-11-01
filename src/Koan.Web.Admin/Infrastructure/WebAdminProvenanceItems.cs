using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Admin.Infrastructure;

internal static class WebAdminProvenanceItems
{
    private static readonly IReadOnlyCollection<string> Consumers = new[]
    {
        "Koan.Web.Admin",
        "Koan.Admin.RouteProvider"
    };

    internal static readonly ProvenanceItem AdminUiUrl = new(
        "web.urls.admin.ui",
        "Admin UI URL",
        "Full URL to the Koan Admin dashboard, resolved from application base URL and admin path prefix.",
        DefaultValue: "(detected)",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem AdminApiUrl = new(
        "web.urls.admin.api",
        "Admin API URL",
        "Full URL to the Koan Admin API root, resolved from application base URL and admin path prefix.",
        DefaultValue: "(detected)",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem LaunchKitUrl = new(
        "web.urls.admin.launchkit",
        "LaunchKit URL",
        "Full URL to the LaunchKit export endpoint.",
        DefaultValue: "(detected)",
        DefaultConsumers: Consumers);
}
