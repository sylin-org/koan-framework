using System.Collections.Generic;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Connector.Swagger.Infrastructure;

internal static class SwaggerProvenanceItems
{
    private const string EnabledKey = Constants.Configuration.Section + ":" + Constants.Configuration.Keys.Enabled;
    private const string RoutePrefixKey = Constants.Configuration.Section + ":" + Constants.Configuration.Keys.RoutePrefix;
    private const string RequireAuthKey = Constants.Configuration.Section + ":" + Constants.Configuration.Keys.RequireAuthOutsideDevelopment;
    private const string IncludeXmlCommentsKey = Constants.Configuration.Section + ":" + Constants.Configuration.Keys.IncludeXmlComments;

    private static readonly IReadOnlyCollection<string> Consumers = new[]
    {
        "Koan.Web.Connector.Swagger.Hosting.KoanSwaggerStartupFilter",
        "Koan.Web.Connector.Swagger.Initialization.KoanAutoRegistrar"
    };

    internal static readonly ProvenanceItem Enabled = new(
        EnabledKey,
        "Swagger Enabled",
        "Determines whether Koan's Swagger surfaces register during startup.",
        DefaultValue: KoanEnv.IsProduction ? "false" : "true",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem RoutePrefix = new(
        RoutePrefixKey,
        "Swagger Route Prefix",
        "Root path for the Swagger UI and JSON endpoints.",
        DefaultValue: "swagger",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem RequireAuthOutsideDevelopment = new(
        RequireAuthKey,
        "Require Auth Outside Development",
        "Whether Swagger endpoints enforce authentication beyond Development environments.",
        DefaultValue: "true",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem IncludeXmlComments = new(
        IncludeXmlCommentsKey,
        "Include XML Comments",
        "Controls whether XML documentation files are loaded into Swagger metadata.",
        DefaultValue: "true",
        DefaultConsumers: Consumers);

    internal static readonly ProvenanceItem SwaggerUrl = new(
        "web.urls.swagger",
        "Swagger UI URL",
        "Full URL to the Swagger UI endpoint, resolved from application base URL and route prefix.",
        DefaultValue: "(detected)",
        DefaultConsumers: Consumers);
}
