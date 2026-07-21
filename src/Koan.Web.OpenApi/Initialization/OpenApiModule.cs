using System;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Core.Modules;
using Koan.Web.OpenApi.Hosting;
using Koan.Web.OpenApi.Infrastructure;
using Koan.Web.OpenApi.Options;
using Koan.Web.OpenApi.Transformers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

namespace Koan.Web.OpenApi.Initialization;

/// <summary>
/// Auto-registrar for Koan's OpenAPI integration.
/// </summary>
public sealed class OpenApiModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind options with deterministic defaults.
        services.AddKoanOptions<KoanOpenApiOptions>(Constants.Configuration.Section);

        // Register Microsoft.AspNetCore.OpenApi pipeline with OpenAPI 3.1 as the default.
        services.AddEndpointsApiExplorer();
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer<ApplicationIdentityDocumentTransformer>();
            options.AddOperationTransformer<PaginationOperationTransformer>();
            options.AddOperationTransformer<KoanHeadersOperationTransformer>();
            options.AddOperationTransformer<TransformerMediaTypesOperationTransformer>();
        });

        // X-openapi-newtonsoft-fidelity: the OpenAPI document is generated from System.Text.Json metadata, but Koan
        // serializes REST via Newtonsoft (camelCase + StringEnumConverter + [JsonProperty] renames). Mirror the
        // Newtonsoft wire onto the STJ options the generator reads — Http.Json.JsonOptions, which is what
        // Microsoft.AspNetCore.OpenApi consults — so the doc is faithful BY CONSTRUCTION (string enums, real wire
        // names) without per-schema doc transformers. The Newtonsoft formatter still owns the REST wire; these
        // options also govern minimal-API responses, which now share the same string-enum contract.
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => Schema.NewtonsoftSchemaMirror.Apply(o.SerializerOptions));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, KoanOpenApiStartupFilter>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var settings = ResolveOptions(cfg, env);

        module.AddSetting(
            Constants.Provenance.SpecVersion,
            ProvenancePublicationMode.Auto,
            "3.1",
            usedDefault: true);

        module.AddSetting(
            Constants.Provenance.Enabled,
            ProvenancePublicationMode.Auto,
            settings.DocumentEnabled.ToString(),
            sourceKey: Constants.Configuration.Enabled,
            usedDefault: settings.Enabled is null);

        module.AddSetting(
            Constants.Provenance.Route,
            ProvenancePublicationMode.Auto,
            settings.DocumentRoute,
            sourceKey: Constants.Configuration.RoutePattern,
            usedDefault: settings.RoutePattern == KoanOpenApiOptions.DefaultRoutePattern);

        var uiPosture = settings.UiEnabled
            ? $"{settings.UiRoute} ({(settings.RequiresAuthentication ? "authentication required" : "open")})"
            : "disabled";

        module.AddSetting(
            Constants.Provenance.Ui,
            ProvenancePublicationMode.Auto,
            uiPosture,
            sourceKey: Constants.Configuration.EnableUi,
            usedDefault: settings.EnableUi is null);

        if (settings.DocumentEnabled)
        {
            module.AddTool(
                "OpenAPI Document",
                settings.DocumentRoute,
                "HTTP OpenAPI 3.1 document",
                capability: "observability.openapi");
        }

        if (settings.UiEnabled)
        {
            module.AddTool(
                "OpenAPI UI",
                settings.UiRoute,
                settings.RequiresAuthentication ? "Interactive OpenAPI UI; authentication required" : "Interactive OpenAPI UI",
                capability: "observability.openapi.ui");
        }
    }

    private static OpenApiOptionSnapshot ResolveOptions(IConfiguration cfg, IHostEnvironment env)
    {
        var defaults = new KoanOpenApiOptions();
        var enabled = cfg.Read<bool?>(Constants.Configuration.Enabled);
        var enableUi = cfg.Read<bool?>(Constants.Configuration.EnableUi);
        var routePattern = cfg.Read(Constants.Configuration.RoutePattern, defaults.RoutePattern)!;
        var uiRoute = cfg.Read(Constants.Configuration.UiRoute, defaults.UiRoute)!;
        var requireAuthentication = cfg.Read(
            Constants.Configuration.RequireAuthenticationOutsideDevelopment,
            defaults.RequireAuthenticationOutsideDevelopment);

        return new OpenApiOptionSnapshot(
            enabled,
            enableUi,
            routePattern,
            uiRoute,
            requireAuthentication,
            env.IsDevelopment());
    }

    private sealed record OpenApiOptionSnapshot(
        bool? Enabled,
        bool? EnableUi,
        string RoutePattern,
        string UiRoutePrefix,
        bool RequireAuthenticationOutsideDevelopment,
        bool IsDevelopment)
    {
        public bool DocumentEnabled => Enabled ?? true;
        public bool UiEnabled => DocumentEnabled && (EnableUi ?? IsDevelopment);
        public bool RequiresAuthentication => UiEnabled && !IsDevelopment && RequireAuthenticationOutsideDevelopment;
        public string DocumentRoute => RoutePattern.Replace("{documentName}", KoanOpenApiOptions.DefaultDocumentName, StringComparison.OrdinalIgnoreCase);
        public string UiRoute => "/" + UiRoutePrefix.Trim('/');
    }
}
