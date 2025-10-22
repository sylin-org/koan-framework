using System;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeMode.Json; // Added for AddCodeModeJson extension
using Koan.Mcp.CodeExecution;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.Diagnostics;
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
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.TryAddSingleton<McpEntityRegistry>();
        services.TryAddSingleton<RequestTranslator>();
        services.TryAddSingleton<ResponseTranslator>();
        services.TryAddSingleton<EndpointToolExecutor>();
        services.TryAddSingleton<IMcpTransportDispatcher, StreamJsonRpcTransportDispatcher>();
        services.TryAddSingleton<McpServer>();
        services.AddHostedService<StdioTransport>();

        services.TryAddSingleton<HttpSseSessionManager>();
        services.AddHostedService(sp => sp.GetRequiredService<HttpSseSessionManager>());
        services.TryAddSingleton<HttpSseTransport>();
        services.TryAddSingleton<IMcpCapabilityReporter, HttpSseCapabilityReporter>();

        services.AddCors();

    // Code mode services
    // JSON facade (Newtonsoft-backed) for code-mode dynamic operations
    services.AddCodeModeJson();
        services.AddOptions<CodeModeOptions>().BindConfiguration("Koan:Mcp:CodeMode");
        services.AddOptions<SandboxOptions>().BindConfiguration("Koan:Mcp:CodeMode:Sandbox");
        services.AddOptions<TypeScriptSdkOptions>().BindConfiguration("Koan:Mcp:CodeMode:TypeScript");
        services.TryAddSingleton<Koan.Mcp.CodeExecution.ICodeExecutor, JintCodeExecutor>();
        services.TryAddSingleton<TypeScriptSdkGenerator>();
        services.TryAddSingleton<TypeScriptSdkProvider>();
        services.TryAddSingleton<ITypeScriptSdkProvider>(sp => sp.GetRequiredService<TypeScriptSdkProvider>());
    services.TryAddScoped<KoanSdkBindings>();

        return services;
    }
}
