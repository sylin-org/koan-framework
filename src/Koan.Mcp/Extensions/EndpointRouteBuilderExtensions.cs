using System;
using Koan.Mcp.Diagnostics;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            .Produces("text/event-stream")
            .WithName("KoanMcpSseStream");

        group.MapPost("rpc", transport.SubmitRequestAsync)
            .Produces("application/json")
            .WithName("KoanMcpRpcSubmit");

        if (options.PublishCapabilityEndpoint && capabilityReporter is not null)
        {
            group.MapGet("capabilities", async context =>
            {
                var document = await capabilityReporter.GetCapabilitiesAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(document, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            })
            .Produces("application/json")
            .WithName("KoanMcpCapabilities");
        }

        return endpoints;
    }
}
