using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Web.OpenApi.Infrastructure;
using Koan.Web.OpenApi.Options;

namespace Koan.Web.OpenApi.Hosting;

/// <summary>
/// Ensures Koan's OpenAPI endpoint is automatically mapped once the application starts.
/// </summary>
internal sealed class KoanOpenApiStartupFilter(IOptions<KoanOpenApiOptions> options) : IStartupFilter
{
    private readonly IOptions<KoanOpenApiOptions> _options = options;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            const string appliedKey = "Koan.Web.OpenApi.Applied";
            if (app.Properties.ContainsKey(appliedKey))
            {
                return;
            }

            app.Properties[appliedKey] = true;

            var cfg = app.ApplicationServices.GetService<IConfiguration>();
            var env = app.ApplicationServices.GetService<IHostEnvironment>();
            var logger = app.ApplicationServices.GetService<ILogger<KoanOpenApiStartupFilter>>();
            var resolved = ResolveOptions(cfg);

            var enabled = ResolveEnabled(resolved, cfg, env);
            if (!enabled)
            {
                logger?.LogInformation("Koan.Web.OpenApi disabled for environment {EnvironmentName}", env?.EnvironmentName);
                return;
            }

            if (TryGetEndpointRouteBuilder(app, out var endpoints))
            {
                endpoints.MapOpenApi(resolved.RoutePattern);
                logger?.LogInformation("Koan.Web.OpenApi mapped {Pattern} (document={Document})", resolved.RoutePattern, KoanOpenApiOptions.DefaultDocumentName);
            }
            else
            {
                logger?.LogWarning("Koan.Web.OpenApi could not locate an endpoint route builder; ensure you host with WebApplication");
            }
        };
    }

    private KoanOpenApiOptions ResolveOptions(IConfiguration? configuration)
    {
        var baseOptions = _options.Value ?? new KoanOpenApiOptions();
        var options = new KoanOpenApiOptions
        {
            Enabled = baseOptions.Enabled,
            RoutePattern = baseOptions.RoutePattern
        };
        if (configuration is null)
        {
            return options;
        }

        // ADR-0040: use explicit reads rather than binding.
        options.Enabled = configuration.Read<bool?>(Constants.Configuration.Enabled, options.Enabled);
        options.RoutePattern = configuration.Read(
            $"{Constants.Configuration.Section}:{Constants.Configuration.Keys.RoutePattern}",
            options.RoutePattern)!;
        return options;
    }

    private static bool ResolveEnabled(KoanOpenApiOptions options, IConfiguration? configuration, IHostEnvironment? env)
    {
        if (options.Enabled.HasValue)
        {
            return options.Enabled.Value;
        }

        // Referencing Koan.Web.OpenApi indicates intent; keep the document on unless explicitly disabled.
        // Allow the global magic flag to opt-in when configuration bindings run before options.
        var magic = configuration?.Read<bool?>(Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction);
        if (magic.HasValue)
        {
            return magic.Value;
        }

        return true;
    }

    private static bool TryGetEndpointRouteBuilder(IApplicationBuilder app, [NotNullWhen(true)] out IEndpointRouteBuilder? endpoints)
    {
        if (app.Properties.TryGetValue("__EndpointRouteBuilder", out var routeBuilder) && routeBuilder is IEndpointRouteBuilder builder)
        {
            endpoints = builder;
            return true;
        }

        endpoints = app as IEndpointRouteBuilder ?? app.Properties.Values.OfType<IEndpointRouteBuilder>().FirstOrDefault();
        return endpoints is not null;
    }
}
