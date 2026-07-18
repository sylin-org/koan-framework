using System;
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

        // AI-0037 D-C — route ownership. The CORE owns the bare GET {baseRoute}: an MCP client (text/event-stream)
        // gets the Streamable server-push stream; a browser (text/html) is delegated to the IMcpConsoleRenderer
        // seam (the WEB-0072 human console). Streamable HTTP is the modern default (StreamableHttpEnabled); the
        // deprecated legacy /sse+/rpc pair is a separate opt-in (EnableLegacySseTransport).
        var streamableOn = options.StreamableHttpEnabled;
        var legacyOn = options.EnableLegacySseTransport;
        var rendererPresent = services.GetService<IMcpConsoleRenderer>() is not null;

        if (!streamableOn && !legacyOn && !rendererPresent)
        {
            return endpoints;
        }

        var baseRoute = string.IsNullOrWhiteSpace(options.HttpSseRoute) ? "/mcp" : options.HttpSseRoute.TrimEnd('/');
        if (string.IsNullOrEmpty(baseRoute))
        {
            baseRoute = "/mcp";
        }
        var configuredResource = options.ResourceUri;

        void ApplyCors(IEndpointConventionBuilder builder)
        {
            if (options.EnableCors && options.AllowedOrigins.Length > 0)
            {
                builder.RequireCors(policy =>
                {
                    policy.WithOrigins(options.AllowedOrigins)
                          .AllowCredentials()
                          .WithHeaders("Authorization", "Content-Type", HttpSseHeaders.SessionId, "Mcp-Session-Id", "MCP-Protocol-Version", "Last-Event-ID")
                          .WithExposedHeaders("Mcp-Session-Id")
                          .WithMethods("GET", "POST", "DELETE", "OPTIONS");
                });
            }
        }

        // The bare GET {baseRoute} — mapped on the ROOT builder (OUTSIDE the auth group) so the console (HTML)
        // branch stays anonymous (the discoverable human face); HandleGet bearer-gates only the SSE-stream branch
        // inline. Mounted when Streamable is on (the stream) OR a console renderer is registered (the human face).
        if (streamableOn || rendererPresent)
        {
            var streamable = services.GetRequiredService<StreamableHttpTransport>();
            var bareGet = endpoints.MapGet(baseRoute, context => streamable.HandleGet(context))
                .WithName("KoanMcpBareGet")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"))
                .ExcludeFromDescription();
            ApplyCors(bareGet);
        }

        // The authenticated route group — POST/DELETE (Streamable), /sse+/rpc (legacy), well-known, capabilities, health.
        if (streamableOn || legacyOn)
        {
            var capabilityReporter = services.GetService<IMcpCapabilityReporter>();
            var group = endpoints.MapGroup(baseRoute);
            ApplyCors(group);

            if (options.RequireAuthentication)
            {
                // SEC-0006 D2/D3 — gate the group by authenticating the trust-fabric bearer scheme explicitly (not
                // RequireAuthorization, whose generic 401 would pre-empt the RFC 9728 challenge). The filter lands the
                // bearer identity in context.User and enforces aud == this resource.
                group.AddEndpointFilter(async (efic, next) =>
                {
                    if (!await McpEdgeAuth.EnsureAuthorized(efic.HttpContext, baseRoute, requireAuth: true, configuredResource))
                        return Results.Empty; // EnsureAuthorized already wrote the 401 + WWW-Authenticate
                    return await next(efic);
                });
            }

            // SEC-0006 D2 (RFC 9728) — public protected-resource metadata at the well-known root (NOT under the
            // authenticated group) so an unauthenticated client can discover the Authorization Server.
            endpoints.MapGet($"/.well-known/oauth-protected-resource{baseRoute}", context =>
            {
                var doc = new
                {
                    resource = McpResourceIdentity.Resolve(context, baseRoute, configuredResource),
                    authorization_servers = new[] { McpResourceIdentity.AuthorizationServer(context) },
                    bearer_methods_supported = new[] { "header" },
                };
                return context.Response.WriteAsJsonAsync(doc, cancellationToken: context.RequestAborted);
            })
            .WithName("KoanMcpProtectedResourceMetadata")
            .WithMetadata(new ProducesResponseTypeAttribute(typeof(object), StatusCodes.Status200OK, "application/json"))
            .ExcludeFromDescription();

            // AI-0037 — the current Streamable HTTP transport. The bare GET is on the root builder (above); POST +
            // DELETE live in the authenticated group.
            if (streamableOn)
            {
                var streamable = services.GetRequiredService<StreamableHttpTransport>();

                group.MapPost("", context => streamable.HandlePost(context))
                    .WithName("KoanMcpStreamablePost")
                    .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"));

                group.MapDelete("", context => streamable.HandleDelete(context))
                    .WithName("KoanMcpStreamableDelete")
                    .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "application/json"));
            }

            // Legacy 2024-11-05 HTTP+SSE transport (deprecated, opt-in): the {baseRoute}/sse + {baseRoute}/rpc pair.
            if (legacyOn)
            {
                var transport = services.GetRequiredService<HttpSseTransport>();

                group.MapGet("sse", context => transport.AcceptStream(context))
                    .WithName("KoanMcpSseStream")
                    .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"));

                group.MapPost("rpc", async context =>
                {
                    var result = await transport.SubmitRequest(context);
                    await result.ExecuteAsync(context);
                })
                    .WithName("KoanMcpRpcSubmit")
                    .WithMetadata(new ProducesResponseTypeAttribute(typeof(IResult), StatusCodes.Status202Accepted, "application/json"));
            }

            if (options.PublishCapabilityEndpoint && capabilityReporter is not null)
            {
                group.MapGet("capabilities", async context =>
                {
                    var document = await capabilityReporter.GetCapabilities(context.RequestAborted);
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
                        enabled = codeModeOpts?.Enabled ?? false,
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
            if (codeModeOptions?.Enabled == true && codeModeOptions.PublishSdkEndpoint)
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

                    group.MapGet("sdk/definitions", context =>
                    {
                        var defs = provider.Current ?? sdkGenerator.GenerateDefinitions(registry.Registrations);
                        context.Response.ContentType = "application/typescript";
                        return context.Response.WriteAsync(defs, context.RequestAborted);
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
