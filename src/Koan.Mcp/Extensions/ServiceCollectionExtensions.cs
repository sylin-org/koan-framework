using System;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Mcp.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Mcp.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanMcp(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configuration is not null)
        {
            services.AddOptions<McpServerOptions>().Bind(configuration.GetSection("Koan:Mcp"));
        }
        else
        {
            services.AddOptions<McpServerOptions>().BindConfiguration("Koan:Mcp");
        }

        services.TryAddSingleton<SchemaBuilder>();
        services.TryAddSingleton<DescriptorMapper>();
        services.TryAddSingleton<McpEntityRegistry>();
        services.TryAddSingleton<EndpointToolExecutor>();
        services.TryAddSingleton<IMcpTransportDispatcher, StreamJsonRpcTransportDispatcher>();
        services.AddHostedService<StdioTransport>();

        return services;
    }
}
