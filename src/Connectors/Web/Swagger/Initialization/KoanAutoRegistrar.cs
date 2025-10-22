using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Connector.Swagger.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;
using SwaggerItems = Koan.Web.Connector.Swagger.Infrastructure.SwaggerProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Web.Connector.Swagger.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Connector.Swagger";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Guard to prevent duplicate Swagger registration if the app already called AddKoanSwagger()
        if (!services.Any(d => d.ServiceType == typeof(ISwaggerProvider)))
        {
            services.AddKoanSwagger();
        }
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

        var magic = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction,
            false);

        var enabledEffective = magic.Value ? true : enabled.Value;
        if (magic.Value)
        {
            module.AddNote("AllowMagicInProduction forced Swagger enabled");
        }

        Publish(
            module,
            SwaggerItems.Enabled,
            enabled,
            displayOverride: enabledEffective,
            usedDefaultOverride: magic.Value ? false : null);

        Publish(module, SwaggerItems.RoutePrefix, routePrefix);
        Publish(module, SwaggerItems.RequireAuthOutsideDevelopment, requireAuth);
        Publish(module, SwaggerItems.IncludeXmlComments, includeXmlComments);
    }

    private static void Publish<T>(
        ProvenanceModuleWriter module,
        ProvenanceItem item,
        ConfigurationValue<T> value,
        object? displayOverride = null,
        ProvenancePublicationMode? modeOverride = null,
        bool? usedDefaultOverride = null,
        string? sourceKeyOverride = null,
        bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            modeOverride ?? ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }
}


