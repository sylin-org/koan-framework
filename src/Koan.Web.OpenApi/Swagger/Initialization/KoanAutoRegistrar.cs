using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Connector.Swagger.Infrastructure;
using SwaggerItems = Koan.Web.Connector.Swagger.Infrastructure.SwaggerProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;
using Koan.Web;

namespace Koan.Web.Connector.Swagger.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Connector.Swagger";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanSwagger();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanSwaggerStartupFilter>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var enabled = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Constants.Configuration.Enabled,
            KoanEnv.IsProduction ? false : true);

        var routePrefix = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RoutePrefix}",
            "swagger");

        var requireAuth = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RequireAuthOutsideDevelopment}",
            true);

        var includeXmlComments = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.IncludeXmlComments}",
            true);

        var documentDisplayName = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.DocumentDisplayName}",
            null as string);

        var magic = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction,
            false);

        var enabledEffective = magic.Value ? true : enabled.Value;
        if (magic.Value)
        {
            module.AddNote("AllowMagicInProduction forced Swagger enabled");
        }

        module.PublishConfigValue(
            SwaggerItems.Enabled,
            enabled,
            displayOverride: enabledEffective,
            usedDefaultOverride: magic.Value ? false : null);

        module.PublishConfigValue(SwaggerItems.RoutePrefix, routePrefix);
        module.PublishConfigValue(SwaggerItems.RequireAuthOutsideDevelopment, requireAuth);
        module.PublishConfigValue(SwaggerItems.IncludeXmlComments, includeXmlComments);
        module.PublishConfigValue(SwaggerItems.DocumentDisplayName, documentDisplayName);

        // Report full Swagger URL for immediate discoverability
        if (enabledEffective)
        {
            var swaggerUrl = KoanWeb.Urls.Build(routePrefix.Value ?? "swagger", cfg, env);
            module.AddSetting(
                SwaggerItems.SwaggerUrl,
                ProvenancePublicationMode.Custom,
                swaggerUrl,
                sourceKey: "Resolved from ApplicationUrl and RoutePrefix",
                usedDefault: false);
        }
    }
}


