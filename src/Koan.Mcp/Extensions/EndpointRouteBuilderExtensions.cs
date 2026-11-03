using System;
using System.Collections.Generic;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.Diagnostics;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Initialization;

internal static class McpEndpointMapping
{
    internal static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));

        var services = endpoints.ServiceProvider;
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<McpServerOptions>>();
        var options = optionsMonitor.CurrentValue;

        // AI-0037 D-C — route ownership. The CORE owns the bare GET {baseRoute}: a browser (text/html) is delegated to the
        // IMcpConsoleRenderer seam (the WEB-0072 human console), and anything else is told there is no stream (SEC-0039).
        var streamableOn = options.EnableStreamableHttpTransport;
        var rendererPresent = services.GetService<IMcpConsoleRenderer>() is not null;

        if (!streamableOn && !rendererPresent)
        {
            return endpoints;
        }

        var baseRoute = string.IsNullOrWhiteSpace(options.HttpRoute) ? "/mcp" : options.HttpRoute.TrimEnd('/');
        if (string.IsNullOrEmpty(baseRoute))
        {
            baseRoute = "/mcp";
        }
        // An application that exposes tools alone gets no code mode's SDK, published or written, whatever CodeMode:Enabled says.
        var codeModeOn = McpExposure.ExposesCode(McpExposure.Resolve(options));
        var streamable = services.GetRequiredService<StreamableHttpTransport>();

        void ApplyCors(IEndpointConventionBuilder builder)
        {
            if (options.EnableCors && options.AllowedOrigins.Length > 0)
            {
                builder.RequireCors(policy =>
                {
                    policy.WithOrigins(options.AllowedOrigins)
                          .AllowCredentials()
                          .WithHeaders("Authorization", "Content-Type", "MCP-Protocol-Version")
                          .WithMethods("GET", "POST", "OPTIONS");
                });
            }
        }

        // The bare GET {baseRoute} — mapped on the ROOT builder (OUTSIDE the auth group) so the console (HTML)
        // branch stays anonymous (the discoverable human face).
        var bareGet = endpoints.MapGet(baseRoute, context => streamable.HandleGet(context))
            .WithName("KoanMcpBareGet")
            .ExcludeFromDescription();
        ApplyCors(bareGet);

        // The route group — POST, capabilities, health. SEC-0039: the edge has no authentication of its own. The caller is
        // whoever the application's authentication found on this request, a visitor when nobody, judged by each tool's gate.
        if (streamableOn)
        {
            var capabilityReporter = services.GetService<IMcpCapabilityReporter>();
            var group = endpoints.MapGroup(baseRoute);
            ApplyCors(group);

            // A door the application closes answers as its own authentication does when nobody is signed in.
            if (options.RequireAuthentication) group.RequireAuthorization();

            // AI-0037, SEC-0039 — one POST is one request, answered as JSON. No DELETE: there is no session to end, so
            // routing answers 405.
            group.MapPost("", context => streamable.HandlePost(context))
                .WithName("KoanMcpStreamablePost")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "application/json"));

            if (options.PublishCapabilityEndpoint && capabilityReporter is not null)
            {
                group.MapGet("capabilities", async context =>
                {
                    // SEC-0018: the tools this request's principal may call, not the registry.
                    var document = await capabilityReporter.GetCapabilitiesFor(StreamableHttpTransport.RequestPrincipal(context), context.RequestAborted);
                    await context.Response.WriteAsJsonAsync(document, cancellationToken: context.RequestAborted);
                })
                .WithName("KoanMcpCapabilities")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(McpCapabilityDocument), StatusCodes.Status200OK, "application/json"));
            }

            // Health endpoint (lightweight)
            group.MapGet("health", context =>
            {
                var codeModeOpts = services.GetService<IOptions<CodeModeOptions>>()?.Value;
                var sandboxOpts = services.GetService<IOptions<SandboxOptions>>()?.Value;
                var registry = services.GetService<McpEntityRegistry>();
                var tsProvider = services.GetService<TypeScriptSdkProvider>();

                var payload = new
                {
                    status = "ok",
                    codeMode = new
                    {
                        enabled = codeModeOn && (codeModeOpts?.Enabled ?? false),
                        entities = registry?.Registrations.Count ?? 0,
                        dtsBytes = tsProvider?.Current?.Length ?? 0,
                        sandbox = sandboxOpts is null ? null : new
                        {
                            cpuMs = sandboxOpts.CpuMilliseconds,
                            memoryMb = sandboxOpts.MemoryMegabytes,
                            maxRecursion = sandboxOpts.MaxRecursionDepth,
                            maxCodeLength = sandboxOpts.MaxCodeLength
                        },
                        limits = codeModeOpts is null ? null : new
                        {
                            maxSdkCalls = codeModeOpts.MaxSdkCalls,
                            maxLogEntries = codeModeOpts.MaxLogEntries,
                            requireAnswer = codeModeOpts.RequireAnswer
                        }
                    }
                };

                return context.Response.WriteAsJsonAsync(payload, cancellationToken: context.RequestAborted);
            })
            .WithName("KoanMcpHealth")
            .WithMetadata(new ProducesResponseTypeAttribute(typeof(object), StatusCodes.Status200OK, "application/json"))
            .ExcludeFromDescription();

            // SDK definitions endpoint for code mode
            var codeModeOptions = services.GetService<IOptions<CodeModeOptions>>()?.Value;
            if (codeModeOn && codeModeOptions?.Enabled == true && codeModeOptions.PublishSdkEndpoint)
            {
                var sdkGenerator = services.GetService<TypeScriptSdkGenerator>();
                var registry = services.GetService<McpEntityRegistry>();
                var provider = services.GetService<TypeScriptSdkProvider>();

                if (sdkGenerator is not null && registry is not null && provider is not null)
                {
                    // Lazy initialize cache once
                    if (string.IsNullOrEmpty(provider.Current))
                    {
                        provider.Current = sdkGenerator.GenerateDefinitions(registry.Registrations);
                    }

                    group.MapGet("sdk/definitions", async context =>
                    {
                        // SEC-0018: declare only the entities this request's principal can call at least one verb on.
                        var gates = context.RequestServices.GetRequiredService<Koan.Web.Authorization.IAccessGateCache>();
                        using var visibility = Koan.Mcp.Execution.McpVerbVisibility.For(
                            context.RequestServices.GetRequiredService<IServiceScopeFactory>(), gates,
                            StreamableHttpTransport.RequestPrincipal(context));
                        var visible = new List<McpEntityRegistration>();
                        foreach (var registration in registry.Registrations)
                        {
                            foreach (var tool in registration.Tools)
                            {
                                if (!await visibility.Allows(registration.EntityType, tool.Operation, context.RequestAborted)) continue;
                                visible.Add(registration);
                                break;
                            }
                        }
                        var defs = sdkGenerator.GenerateDefinitions(visible);
                        context.Response.ContentType = "application/typescript";
                        await context.Response.WriteAsync(defs, context.RequestAborted);
                    })
                    .WithName("KoanMcpSdkDefinitions")
                    .WithMetadata(new ProducesResponseTypeAttribute(typeof(string), StatusCodes.Status200OK, "application/typescript"))
                    .ExcludeFromDescription();
                }
            }
        }

        return endpoints;
    }
}
