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

namespace Koan.Mcp.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapKoanMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));

        var services = endpoints.ServiceProvider;
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<McpServerOptions>>();
        var options = optionsMonitor.CurrentValue;

        // AI-0037 — the two HTTP transports share this route group (and its auth filter + well-known metadata). The
        // Streamable HTTP transport owns the bare {baseRoute} (POST/GET/DELETE); the deprecated legacy transport owns
        // {baseRoute}/sse + {baseRoute}/rpc. Neither mounted unless its switch is on.
        if (!options.EnableHttpSseTransport && !options.EnableStreamableHttpTransport)
        {
            return endpoints;
        }

        var baseRoute = string.IsNullOrWhiteSpace(options.HttpSseRoute) ? "/mcp" : options.HttpSseRoute.TrimEnd('/');
        if (string.IsNullOrEmpty(baseRoute))
        {
            baseRoute = "/mcp";
        }

        var capabilityReporter = services.GetService<IMcpCapabilityReporter>();

        var group = endpoints.MapGroup(baseRoute);

        if (options.EnableCors && options.AllowedOrigins.Length > 0)
        {
            group.RequireCors(policy =>
            {
                policy.WithOrigins(options.AllowedOrigins)
                      .AllowCredentials()
                      .WithHeaders("Authorization", "Content-Type", HttpSseHeaders.SessionId, "Mcp-Session-Id", "MCP-Protocol-Version", "Last-Event-ID")
                      .WithExposedHeaders("Mcp-Session-Id")
                      .WithMethods("GET", "POST", "DELETE", "OPTIONS");
            });
        }

        var configuredResource = options.ResourceUri;
        if (options.RequireAuthentication)
        {
            // SEC-0006 D2/D3 — gate the whole group by authenticating the trust-fabric bearer scheme explicitly
            // (not RequireAuthorization, whose generic 401 would pre-empt the RFC 9728 challenge). The filter
            // lands the bearer identity in context.User and enforces aud == this resource; from there the
            // existing OriginStamp → HttpSseSession.User → SEC-0004/0005 chain runs unchanged.
            group.AddEndpointFilter(async (efic, next) =>
            {
                if (!await McpEdgeAuth.EnsureAuthorized(efic.HttpContext, baseRoute, requireAuth: true, configuredResource))
                    return Results.Empty; // EnsureAuthorized already wrote the 401 + WWW-Authenticate
                return await next(efic);
            });
        }

        // SEC-0006 D2 (RFC 9728) — public protected-resource metadata at the well-known root (NOT under the
        // authenticated group) so an unauthenticated client can discover the Authorization Server. Mounted at
        // /.well-known/oauth-protected-resource{baseRoute}; the WWW-Authenticate header on the 401 points here.
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

        // AI-0037 — the current Streamable HTTP transport: a single endpoint at the bare {baseRoute}.
        if (options.EnableStreamableHttpTransport)
        {
            var streamable = services.GetRequiredService<StreamableHttpTransport>();

            group.MapPost("", context => streamable.HandlePost(context))
                .WithName("KoanMcpStreamablePost")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"));

            group.MapGet("", context => streamable.HandleGet(context))
                .WithName("KoanMcpStreamableGet")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"));

            group.MapDelete("", context => streamable.HandleDelete(context))
                .WithName("KoanMcpStreamableDelete")
                .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "application/json"));
        }

        // Legacy 2024-11-05 HTTP+SSE transport (deprecated): the {baseRoute}/sse + {baseRoute}/rpc pair.
        if (options.EnableHttpSseTransport)
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

        return endpoints;
    }
}
