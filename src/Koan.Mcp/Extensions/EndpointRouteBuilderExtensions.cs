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

        if (!options.EnableHttpSseTransport)
        {
            return endpoints;
        }

        var baseRoute = string.IsNullOrWhiteSpace(options.HttpSseRoute) ? "/mcp" : options.HttpSseRoute.TrimEnd('/');
        if (string.IsNullOrEmpty(baseRoute))
        {
            baseRoute = "/mcp";
        }

        var transport = services.GetRequiredService<HttpSseTransport>();
        var capabilityReporter = services.GetService<IMcpCapabilityReporter>();

        var group = endpoints.MapGroup(baseRoute);

        if (options.EnableCors && options.AllowedOrigins.Length > 0)
        {
            group.RequireCors(policy =>
            {
                policy.WithOrigins(options.AllowedOrigins)
                      .AllowCredentials()
                      .WithHeaders("Authorization", "Content-Type", HttpSseHeaders.SessionId)
                      .WithMethods("GET", "POST", "OPTIONS");
            });
        }

        if (options.RequireAuthentication)
        {
            group.RequireAuthorization();
        }

        group.MapGet("sse", transport.AcceptStreamAsync)
            .WithName("KoanMcpSseStream")
            .WithMetadata(new ProducesResponseTypeAttribute(typeof(void), StatusCodes.Status200OK, "text/event-stream"));

        group.MapPost("rpc", transport.SubmitRequestAsync)
            .WithName("KoanMcpRpcSubmit")
            .WithMetadata(new ProducesResponseTypeAttribute(typeof(IResult), StatusCodes.Status202Accepted, "application/json"));

        if (options.PublishCapabilityEndpoint && capabilityReporter is not null)
        {
            group.MapGet("capabilities", async context =>
            {
                var document = await capabilityReporter.GetCapabilitiesAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(document, cancellationToken: context.RequestAborted).ConfigureAwait(false);
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
