using System;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
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
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Web.OpenApi";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

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

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var settings = ResolveOptions(cfg);

        module.AddSetting(
            Constants.Provenance.SpecVersion,
            ProvenancePublicationMode.Auto,
            "3.1",
            usedDefault: true);

        module.AddSetting(
            Constants.Provenance.Route,
            ProvenancePublicationMode.Auto,
            settings.CurrentRoute,
            sourceKey: settings.SourceRoutePatternKey,
            usedDefault: settings.RoutePattern == KoanOpenApiOptions.DefaultRoutePattern);

        module.AddTool(
            "OpenAPI Document",
            settings.CurrentRoute,
            "HTTP OpenAPI document exposed by Koan.Web.OpenApi",
            capability: "observability.openapi");
    }

    private static OpenApiOptionSnapshot ResolveOptions(IConfiguration cfg)
    {
        var options = new KoanOpenApiOptions();
        var routePatternKey = $"{Infrastructure.Constants.Configuration.Section}:{Infrastructure.Constants.Configuration.Keys.RoutePattern}";

        options.RoutePattern = cfg.Read(routePatternKey, options.RoutePattern)!;

        return new OpenApiOptionSnapshot(
            options.RoutePattern,
            routePatternKey);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }

    private sealed record OpenApiOptionSnapshot(
        string RoutePattern,
        string SourceRoutePatternKey)
    {
        public string CurrentRoute => RoutePattern.Replace("{documentName}", KoanOpenApiOptions.DefaultDocumentName, StringComparison.OrdinalIgnoreCase);
    }
}
