using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.OpenApi.Infrastructure;
using Koan.Web.OpenApi.Options;

namespace Koan.Web.OpenApi.Hosting;

/// <summary>
/// Publishes Koan's OpenAPI document and optional interactive UI from one resolved option model.
/// </summary>
internal sealed class KoanOpenApiStartupFilter(
    IOptions<KoanOpenApiOptions> options,
    ILogger<KoanOpenApiStartupFilter> logger) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            if (app.Properties.ContainsKey(Constants.Runtime.AppliedKey))
            {
                next(app);
                return;
            }

            app.Properties[Constants.Runtime.AppliedKey] = true;

            var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
            var resolved = Resolve(options.Value, environment);

            if (resolved.UiEnabled)
            {
                if (resolved.RequiresAuthentication)
                {
                    RequireAuthenticatedUi(app, resolved.UiPath);
                }

                app.UseSwaggerUI(ui =>
                {
                    ui.RoutePrefix = resolved.UiRoutePrefix;
                    ui.SwaggerEndpoint(resolved.DocumentPath, "Koan API v1");
                });

                logger.LogInformation(
                    "Koan OpenAPI UI mapped {UiPath} (authentication={Authentication})",
                    resolved.UiPath,
                    resolved.RequiresAuthentication ? "required" : "not-required");
            }
            else
            {
                logger.LogInformation("Koan OpenAPI UI disabled for environment {EnvironmentName}", environment.EnvironmentName);
            }

            next(app);

            if (!resolved.DocumentEnabled)
            {
                logger.LogInformation("Koan OpenAPI document disabled");
                return;
            }

            if (TryGetEndpointRouteBuilder(app, out var endpoints))
            {
                endpoints.MapOpenApi(resolved.RoutePattern);
                logger.LogInformation(
                    "Koan OpenAPI 3.1 document mapped {DocumentPath} (document={DocumentName})",
                    resolved.DocumentPath,
                    KoanOpenApiOptions.DefaultDocumentName);
            }
            else
            {
                logger.LogWarning(
                    "Koan OpenAPI could not locate an endpoint route builder; host with WebApplication or ASP.NET endpoint routing");
            }
        };
    }

    private void RequireAuthenticatedUi(IApplicationBuilder app, string uiPath)
    {
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(uiPath),
            branch => branch.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                try
                {
                    var result = await context.AuthenticateAsync().ConfigureAwait(false);
                    if (result.Succeeded && result.Principal is not null)
                    {
                        context.User = result.Principal;
                        await next(context).ConfigureAwait(false);
                        return;
                    }
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogDebug(exception, "OpenAPI UI authentication has no usable default scheme");
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "OpenAPI UI requires authentication outside Development. Configure a default authentication scheme " +
                    $"or deliberately set {KoanOpenApiOptions.SectionPath}:RequireAuthenticationOutsideDevelopment=false.",
                    context.RequestAborted).ConfigureAwait(false);
            }));
    }

    private static ResolvedOpenApiOptions Resolve(KoanOpenApiOptions options, IHostEnvironment environment)
    {
        var documentEnabled = options.Enabled ?? true;
        var uiEnabled = documentEnabled && (options.EnableUi ?? environment.IsDevelopment());

        if (documentEnabled && string.IsNullOrWhiteSpace(options.RoutePattern))
        {
            throw new InvalidOperationException(
                $"{KoanOpenApiOptions.SectionPath}:RoutePattern cannot be empty while the OpenAPI document is enabled.");
        }

        if (uiEnabled && string.IsNullOrWhiteSpace(options.UiRoute))
        {
            throw new InvalidOperationException(
                $"{KoanOpenApiOptions.SectionPath}:UiRoute cannot be empty while the OpenAPI UI is enabled.");
        }

        var routePattern = EnsureLeadingSlash(options.RoutePattern);
        var documentPath = routePattern.Replace(
            "{documentName}",
            KoanOpenApiOptions.DefaultDocumentName,
            StringComparison.OrdinalIgnoreCase);
        var uiRoutePrefix = options.UiRoute.Trim('/');

        return new ResolvedOpenApiOptions(
            documentEnabled,
            uiEnabled,
            uiEnabled && !environment.IsDevelopment() && options.RequireAuthenticationOutsideDevelopment,
            routePattern,
            documentPath,
            uiRoutePrefix,
            "/" + uiRoutePrefix);
    }

    private static string EnsureLeadingSlash(string path)
        => path.StartsWith('/') ? path : "/" + path;

    private static bool TryGetEndpointRouteBuilder(
        IApplicationBuilder app,
        [NotNullWhen(true)] out IEndpointRouteBuilder? endpoints)
    {
        if (app.Properties.TryGetValue("__EndpointRouteBuilder", out var routeBuilder)
            && routeBuilder is IEndpointRouteBuilder builder)
        {
            endpoints = builder;
            return true;
        }

        endpoints = app as IEndpointRouteBuilder
            ?? app.Properties.Values.OfType<IEndpointRouteBuilder>().FirstOrDefault();
        return endpoints is not null;
    }

    private sealed record ResolvedOpenApiOptions(
        bool DocumentEnabled,
        bool UiEnabled,
        bool RequiresAuthentication,
        string RoutePattern,
        string DocumentPath,
        string UiRoutePrefix,
        string UiPath);
}
